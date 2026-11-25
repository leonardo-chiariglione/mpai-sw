using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Mmc.Soe;
using Xunit;

// THE NAME OF A TRANSLATED SPEECH (2026-11-25): the name of the original with the two-letter code of the language it is translated into, not "-trans"; translated again, the new code
// takes the place of the old one.
public class SpeechTranslatedNameTests
{
    private static BasicSpeechObject In(string? language) => new()
    {
        BasicSpeechObjectID = "x",
        SpeechQualifier = language is null ? null : new SpeechQualifier { Attributes = new SpeechAttributes { Metadata = new SpeechMetadata { Language = new Language { LanguageCode = language } } } }
    };

    [Theory]
    [InlineData("clip777", "en", "fr", "clip777-fr")]          // into French
    [InlineData("clip777", null, "fr-FR", "clip777-fr")]       // a language with its region: its two letters
    [InlineData("clip777-fr", "fr", "de", "clip777-de")]       // a translation translated again: the new code in place of the old
    [InlineData("clip777-trans", "en", "it", "clip777-it")]    // named the earlier way
    [InlineData("hello-en", "fr", "de", "hello-en-de")]        // "-en" is not the code of the language it is in: part of the name the person gave
    public void TheNameSaysTheLanguage(string id, string? from, string to, string expected) =>
        Assert.Equal(expected, SpeechStore.TranslatedName(id, In(from), to));
}
