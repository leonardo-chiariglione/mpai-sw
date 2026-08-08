using System.Threading.Tasks;

using Mpai.Core;

namespace Mpai.Aims.Ocr;

// Optical Character Recognition AIM (MMC-OCR-V2.5).
// Pure function: a Visual Object in, Recognised Text out. No OS access.
public interface IOcrAim
{
    Task<RecognisedText> ProcessAsync(BasicVisualObject image);
}
