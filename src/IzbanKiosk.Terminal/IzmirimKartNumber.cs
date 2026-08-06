using System;
using System.Text;

namespace IzbanKiosk.Terminal
{
    /// <summary>
    /// Renders the card number the way it is printed on the İzmirim Kart, so a
    /// passenger can match what the screen and the receipt show against the card in
    /// their hand.
    ///
    /// The reader returns a ten-digit alias. The card carries eleven digits, grouped
    /// as <c>23400-18133-5</c>: the alias followed by a check digit that is the sum of
    /// the ten alias digits modulo ten. Two physical cards confirm the rule and are
    /// kept as tests; Luhn was ruled out against both.
    /// </summary>
    internal static class IzmirimKartNumber
    {
        private const int AliasLength = 10;

        internal static string Format(string alias)
        {
            string digits = NormaliseAlias(alias);
            if (digits.Length == 0)
            {
                return "-";
            }

            return digits.Substring(0, 5) + "-" + digits.Substring(5, 5) + "-" + CheckDigit(digits);
        }

        /// <summary>
        /// Same grouping, but only the last four alias digits and the check digit are
        /// legible. A receipt can be dropped or left behind; the passenger still needs
        /// enough of the number to recognise their own card.
        /// </summary>
        internal static string Mask(string alias)
        {
            string digits = NormaliseAlias(alias);
            if (digits.Length == 0)
            {
                return "-";
            }

            return "*****-*" + digits.Substring(6, 4) + "-" + CheckDigit(digits);
        }

        /// <summary>
        /// Empty when the value is not a ten-digit alias. An unexpected shape is
        /// reported rather than reformatted: appending a computed check digit to
        /// something that is not an alias would invent a card number.
        /// </summary>
        private static string NormaliseAlias(string alias)
        {
            string value = (alias ?? string.Empty).Trim();
            if (value.Length == 0 || value.Length > AliasLength)
            {
                return string.Empty;
            }

            foreach (char character in value)
            {
                if (!char.IsDigit(character))
                {
                    return string.Empty;
                }
            }

            // The vendor returns the alias as an unsigned integer, so a card whose
            // number begins with zero arrives short.
            return value.PadLeft(AliasLength, '0');
        }

        private static int CheckDigit(string tenDigits)
        {
            int total = 0;
            foreach (char character in tenDigits)
            {
                total += character - '0';
            }
            return total % 10;
        }

        /// <summary>
        /// Falls back to the raw value when it is not a recognisable alias, so an
        /// unexpected reading is still visible to staff instead of vanishing.
        /// </summary>
        internal static string FormatOrRaw(string alias)
        {
            string formatted = Format(alias);
            if (formatted != "-")
            {
                return formatted;
            }
            string value = (alias ?? string.Empty).Trim();
            return value.Length == 0 ? "-" : value;
        }
    }
}
