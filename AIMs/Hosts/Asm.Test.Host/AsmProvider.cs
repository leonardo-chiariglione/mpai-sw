using System;

using AIF.Controller;
using AIF.SharedStorage;
using AIF.Store;
using System.Collections.Generic;
using System.IO;

using Mpai.Aims.Audio;
using Mpai.Cae.Aoe;
using Mpai.Cae.Ase;
using Mpai.Cae.Asd;

namespace Asm.Test.Host;

// Builds the four AIMs of CAE-ASM for the Controller.
//
// Every AIM is constructed here and nowhere else, and none of them is handed
// another: CAE-ASE receives AudioObjects from CAE-AOE across the Topology, which
// is the whole point of the exercise. The engines share one Shared Storage,
// because they share a repository of assets - not because they share code.
public sealed class AsmProvider : IAimProvider
{
    private readonly AmdStore       _store;
    private readonly ISharedStorage _storage;

    private readonly AoeAim _aoe;
    private readonly AseAim _ase;

    private readonly string _outputFolder;

    public AsmProvider(AmdStore store, ISharedStorage storage, string outputFolder)
    {
        _store        = store;
        _storage      = storage;
        _outputFolder = outputFolder;
        _aoe     = new AoeAim(storage);
        _ase     = new AseAim(storage);
    }

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings) => aimName switch
    {
        "CAE-AOE-V1.0" => new AoeAimProcessor(aimName, _aoe, AimPortReader.Load(_store, aimName)),
        "CAE-ASE-V1.0" => new AseAimProcessor(aimName, _ase, AimPortReader.Load(_store, aimName)),
        // AsdAim is real - it places objects for a listener through a delivery
        // AIM. A headless test has no loudspeaker, so a file-based delivery
        // stands in, which is a configuration rather than a compromise.
        "CAE-ASD-V1.0" => new AsdAimProcessor(aimName, new AsdAim(new FileAudioDelivery(_outputFolder)), AimPortReader.Load(_store, aimName)),

        // Acquisition needs a device. A headless test has none, so a file-based
        // acquisition stands in - which is exactly the case CAE-AOA was written
        // to cover, and not a compromise for the test's sake.
        //
        // The path need not exist: these four runs supply no AudioSource, so
        // CAE-AOA is skipped every time. That Port is optional precisely so a
        // run which is not acquiring does not wait for a microphone.
        "CAE-AOA-V1.0" => new AoaAimProcessor(
                              aimName,
                              new FileAudioAcquisition(Path.Combine(_outputFolder, "acquired.wav")),
                              AimPortReader.Load(_store, aimName)),

        _ => throw new NotSupportedException($"{aimName} is not part of CAE-ASM.")
    };
}