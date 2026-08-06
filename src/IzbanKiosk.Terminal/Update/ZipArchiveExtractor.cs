using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace IzbanKiosk.Terminal.Update
{
    /// <summary>
    /// Minimal ZIP reader for the update package.
    ///
    /// .NET Framework 4.0 has no <c>System.IO.Compression.ZipFile</c>, and the kiosk
    /// must not depend on a third-party archiver or on shelling out to Windows: an
    /// unattended machine cannot answer a shell progress dialog. Only what a release
    /// package actually contains is supported - stored and deflated entries.
    /// </summary>
    internal static class ZipArchiveExtractor
    {
        private const uint EndOfCentralDirectorySignature = 0x06054b50;
        private const uint CentralDirectorySignature = 0x02014b50;
        private const uint LocalHeaderSignature = 0x04034b50;

        internal static void ExtractTo(string archivePath, string destinationDirectory)
        {
            byte[] archive = File.ReadAllBytes(archivePath);
            int endRecord = FindEndOfCentralDirectory(archive);
            if (endRecord < 0)
            {
                throw new InvalidDataException("Update package is not a ZIP archive.");
            }

            int entryCount = ReadUInt16(archive, endRecord + 10);
            int directoryOffset = (int)ReadUInt32(archive, endRecord + 16);

            int cursor = directoryOffset;
            for (int i = 0; i < entryCount; i++)
            {
                if (ReadUInt32(archive, cursor) != CentralDirectorySignature)
                {
                    throw new InvalidDataException("Update package central directory is corrupt.");
                }

                int method = ReadUInt16(archive, cursor + 10);
                int compressedSize = (int)ReadUInt32(archive, cursor + 20);
                int uncompressedSize = (int)ReadUInt32(archive, cursor + 24);
                int nameLength = ReadUInt16(archive, cursor + 28);
                int extraLength = ReadUInt16(archive, cursor + 30);
                int commentLength = ReadUInt16(archive, cursor + 32);
                int localHeaderOffset = (int)ReadUInt32(archive, cursor + 42);
                string entryName = Encoding.UTF8.GetString(archive, cursor + 46, nameLength);

                cursor += 46 + nameLength + extraLength + commentLength;

                if (entryName.Length == 0 || entryName.EndsWith("/") || entryName.EndsWith("\\"))
                {
                    continue;
                }

                string targetPath = ResolveSafePath(destinationDirectory, entryName);
                string targetDirectory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDirectory) && !Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                WriteEntry(archive, localHeaderOffset, method, compressedSize, uncompressedSize, targetPath);
            }
        }

        /// <summary>
        /// Rejects entry names that would land outside the destination. A crafted
        /// archive must not be able to overwrite files elsewhere on the kiosk.
        /// </summary>
        private static string ResolveSafePath(string destinationDirectory, string entryName)
        {
            string relative = entryName.Replace('/', Path.DirectorySeparatorChar);
            string root = Path.GetFullPath(destinationDirectory);
            string full = Path.GetFullPath(Path.Combine(root, relative));

            string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? root
                : root + Path.DirectorySeparatorChar;

            if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Update package contains an entry outside the target folder: " + entryName);
            }
            return full;
        }

        private static void WriteEntry(
            byte[] archive, int localHeaderOffset, int method, int compressedSize, int uncompressedSize, string targetPath)
        {
            if (ReadUInt32(archive, localHeaderOffset) != LocalHeaderSignature)
            {
                throw new InvalidDataException("Update package entry header is corrupt.");
            }

            int nameLength = ReadUInt16(archive, localHeaderOffset + 26);
            int extraLength = ReadUInt16(archive, localHeaderOffset + 28);
            int dataOffset = localHeaderOffset + 30 + nameLength + extraLength;

            using (FileStream output = File.Create(targetPath))
            {
                if (method == 0)
                {
                    output.Write(archive, dataOffset, compressedSize);
                    return;
                }

                if (method != 8)
                {
                    throw new InvalidDataException("Update package uses an unsupported compression method: " + method);
                }

                using (var compressed = new MemoryStream(archive, dataOffset, compressedSize, false))
                using (var inflater = new DeflateStream(compressed, CompressionMode.Decompress))
                {
                    var buffer = new byte[81920];
                    int written = 0;
                    int read;
                    while ((read = inflater.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        written += read;
                    }

                    if (written != uncompressedSize)
                    {
                        throw new InvalidDataException("Update package entry did not decompress to its declared size.");
                    }
                }
            }
        }

        private static int FindEndOfCentralDirectory(byte[] archive)
        {
            int minimum = Math.Max(0, archive.Length - 66560);
            for (int i = archive.Length - 22; i >= minimum; i--)
            {
                if (ReadUInt32(archive, i) == EndOfCentralDirectorySignature)
                {
                    return i;
                }
            }
            return -1;
        }

        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset]
                | (buffer[offset + 1] << 8)
                | (buffer[offset + 2] << 16)
                | (buffer[offset + 3] << 24));
        }
    }
}
