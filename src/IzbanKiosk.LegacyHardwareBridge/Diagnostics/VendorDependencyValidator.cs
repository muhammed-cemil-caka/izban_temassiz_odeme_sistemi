using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using System.Collections.Generic;
using IzbanKiosk.LegacyHardwareBridge.Interop;

namespace IzbanKiosk.LegacyHardwareBridge.Diagnostics
{
    public class VendorDependencyValidator
    {
        private readonly string _vendorFolder;
        private readonly string _manifestPath;

        public VendorDependencyValidator()
        {
            // Vendor DLLs expected in the local execution directory or standard subfolder
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            _vendorFolder = Path.Combine(exeDir, "vendor");
            string vendorManifest = Path.Combine(_vendorFolder, "vendor-manifest.local.json");
            string adjacentManifest = Path.Combine(exeDir, "vendor-manifest.local.json");
            _manifestPath = File.Exists(vendorManifest) ? vendorManifest : adjacentManifest;
        }

        public bool Validate(out string errorMessage, out List<string> missingFiles)
        {
            errorMessage = string.Empty;
            missingFiles = new List<string>();

            // 1. Operating System Check
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                errorMessage = $"Supported operating system is Windows. Current: {Environment.OSVersion.Platform}";
                return false;
            }

            // 2. Process Architecture Check
            if (IntPtr.Size != 4) // IntPtr.Size == 4 means 32-bit (x86)
            {
                errorMessage = "This bridge must run in a 32-bit (x86) process context because legacy native DLLs are x86.";
                return false;
            }

            // 3. Structural Marshaling Size Verification (x86 alignment and pack check)
            if (Marshal.SizeOf(typeof(EmvRdr35NativeMethods.CARD_LAYOUT)) != 27)
            {
                errorMessage = $"CARD_LAYOUT struct marshaling size mismatch: expected 27, got {Marshal.SizeOf(typeof(EmvRdr35NativeMethods.CARD_LAYOUT))}";
                return false;
            }
            if (Marshal.SizeOf(typeof(EmvRdr35NativeMethods.AV2_LAYOUT_EXT)) != 62)
            {
                errorMessage = $"AV2_LAYOUT_EXT struct marshaling size mismatch: expected 62, got {Marshal.SizeOf(typeof(EmvRdr35NativeMethods.AV2_LAYOUT_EXT))}";
                return false;
            }
            if (Marshal.SizeOf(typeof(EmvRdr35NativeMethods.AV2_LAYOUT)) != 13)
            {
                errorMessage = $"AV2_LAYOUT struct marshaling size mismatch: expected 13, got {Marshal.SizeOf(typeof(EmvRdr35NativeMethods.AV2_LAYOUT))}";
                return false;
            }
            if (Marshal.SizeOf(typeof(EmvRdr35NativeMethods.TOffCardInf)) != 18)
            {
                errorMessage = $"TOffCardInf struct marshaling size mismatch: expected 18, got {Marshal.SizeOf(typeof(EmvRdr35NativeMethods.TOffCardInf))}";
                return false;
            }

            // 3. WHitelist files check
            string[] requiredDlls = new[]
            {
                "EMVRdr35Lib.dll",
                "KioskPrint.dll",
                "QAsisIzmirimKartLibW.dll",
                "QAsisIzmirimKartLibWNet.dll",
                "QSmartCardLibW.dll",
                "QSmartCardLibWNet.dll",
                "CardLibW.dll",
                "CardLibWNet.dll",
                "QT5Core.dll",
                "libeay32.dll"
            };

            // Check if DLLs are present in either execution folder or vendor/ subfolder
            // Let's resolve the actual dll locations. If path is in 'vendor' folder, our bridge will add 'vendor' to dll search path.
            // For check, we list what is missing.
            string runDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var dllName in requiredDlls)
            {
                bool existsInRunDir = File.Exists(Path.Combine(runDir, dllName));
                bool existsInVendorDir = File.Exists(Path.Combine(_vendorFolder, dllName));

                if (!existsInRunDir && !existsInVendorDir)
                {
                    missingFiles.Add(dllName);
                }
            }

            if (missingFiles.Count > 0)
            {
                errorMessage = $"Missing legacy vendor dependency files: {string.Join(", ", missingFiles)}";
                return false;
            }

            // 4. Manifest Integrity verification (Mandatory)
            if (!File.Exists(_manifestPath))
            {
                errorMessage = "Production integrity failure: manifest file 'vendor-manifest.local.json' is missing.";
                return false;
            }

            try
            {
                string content = File.ReadAllText(_manifestPath);
                var manifestData = JsonConvert.DeserializeObject<ManifestData>(content);
                if (manifestData?.Files == null || manifestData.Files.Count == 0)
                {
                    errorMessage = "Manifest data is empty or invalid.";
                    return false;
                }

                var manifestNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var manifestFile in manifestData.Files)
                {
                    if (!manifestNames.Add(manifestFile.Filename))
                    {
                        errorMessage = $"Duplicate manifest entry found for '{manifestFile.Filename}'.";
                        return false;
                    }
                }

                foreach (string requiredDll in requiredDlls)
                {
                    if (!manifestNames.Contains(requiredDll))
                    {
                        errorMessage = $"Required dependency '{requiredDll}' is not covered by the integrity manifest.";
                        return false;
                    }
                }

                foreach (var mf in manifestData.Files)
                {
                    string targetPath = File.Exists(Path.Combine(runDir, mf.Filename))
                        ? Path.Combine(runDir, mf.Filename)
                        : Path.Combine(_vendorFolder, mf.Filename);

                    if (!File.Exists(targetPath))
                    {
                        errorMessage = $"Manifest file entry '{mf.Filename}' was not found.";
                        return false;
                    }

                    string actualHash = CalculateSha256(targetPath);
                    if (!string.Equals(actualHash, mf.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        errorMessage = $"Checksum verification failed for {mf.Filename}. Expected SHA-256: {mf.Sha256}, Actual: {actualHash}";
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Integrity verification failed with error: {ex.Message}";
                return false;
            }

            return true;
        }

        private void WriteWarning(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[WARN] {msg}");
            Console.ResetColor();
        }

        private string CalculateSha256(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                var sb = new System.Text.StringBuilder();
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        private class ManifestFile
        {
            [JsonProperty("filename")]
            public string Filename { get; set; } = string.Empty;

            [JsonProperty("sha256")]
            public string Sha256 { get; set; } = string.Empty;
        }

        private class ManifestData
        {
            [JsonProperty("files")]
            public List<ManifestFile> Files { get; set; } = new List<ManifestFile>();
        }
    }
}
