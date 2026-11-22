using System;
using System.Collections.Generic;
using System.IO;

using AIF.Controller;
using AIF.Store;

using Mpai.Hmc.Provider;

namespace Mpai.Mmc.Cps;

// THE AIMs OF CONVERSATION WITH PERSONAL STATUS, built for the Controller: Multimodal Frontend Description, Entity Body Description, Visual Object Identification, Automatic Speech Recognition,
// Natural Language Understanding, Personal Status Extraction, Entity Dialogue Processing and Portable Avatar Multiplexing have the same builders as the front end of the Communicating Entities in
// Context; this provider gives the AIM host a type it can start with the Store of the AMDs.
public sealed class CpsProvider : IAimProvider
{
    private readonly CompositeProvider inner;

    public CpsProvider(AmdStore store) => inner = HmcProviders.For(store, Root());

    // The folder of the software: the one that holds AIMs\AMDs.
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "AIMs", "AMDs"))) return d.FullName;
        throw new InvalidOperationException("The folder of the software (the one with AIMs/AMDs) is not above " + AppContext.BaseDirectory);
    }

    public bool CanCreate(string aimName) => inner.CanCreate(aimName);
    public string? ImplementationOf(string aimName) => inner.ImplementationOf(aimName);
    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings, AIF.SharedStorage.ISharedStorage? storage) => inner.Create(aimName, settings, storage);
}
