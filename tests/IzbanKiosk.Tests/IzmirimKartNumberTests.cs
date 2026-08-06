using IzbanKiosk.Win7Prototype;

namespace IzbanKiosk.Tests;

public sealed class IzmirimKartNumberTests
{
    // Two physical cards read on the Alsancak kiosk. The reader alias is what the
    // vendor library returned; the printed value is what is embossed on the card.
    [Theory]
    [InlineData("2340018133", "23400-18133-5")]
    [InlineData("2332816500", "23328-16500-0")]
    public void Format_MatchesTheNumberPrintedOnTheCard(string readerAlias, string printedOnCard)
    {
        Assert.Equal(printedOnCard, IzmirimKartNumber.Format(readerAlias));
    }

    [Fact]
    public void Format_PadsAnAliasWhoseLeadingZeroTheReaderDropped()
    {
        // The vendor returns the alias as an unsigned integer, so a card numbered
        // 02332-81650-x arrives nine digits long.
        Assert.Equal("02332-81650-0", IzmirimKartNumber.Format("233281650"));
    }

    [Fact]
    public void Mask_KeepsTheLastFourDigitsAndTheCheckDigit()
    {
        Assert.Equal("*****-*8133-5", IzmirimKartNumber.Mask("2340018133"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("23400ABCDE")]
    [InlineData("234001813312")]
    public void UnexpectedValues_AreNotReformattedIntoACardNumber(string alias)
    {
        // Appending a computed check digit to something that is not an alias would
        // invent a card number, so these are reported as unknown instead.
        Assert.Equal("-", IzmirimKartNumber.Format(alias));
    }

    [Fact]
    public void FormatOrRaw_ShowsAnUnrecognisedReadingInsteadOfHidingIt()
    {
        Assert.Equal("23400ABCDE", IzmirimKartNumber.FormatOrRaw("23400ABCDE"));
    }
}
