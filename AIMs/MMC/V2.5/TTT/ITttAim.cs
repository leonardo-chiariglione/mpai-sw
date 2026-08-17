using Mpai.Core;

namespace Mmc.Ttt;

// MMC-TTT-V2.5 â€” Text-to-Text Translation. The engine-facing contract.
public interface ITttAim
{
    Task<BasicTextObject> ProcessAsync(
        BasicTextObject     text,
        BasicSelectorObject languages,
        CancellationToken   token = default);
}