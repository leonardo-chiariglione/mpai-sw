using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Cae.Asd;

// ---------------------------------------------------------------------------
//  CAE-ASD-V1.0 - Audio Scene Delivery.
//
//  Takes a fully materialized AudioObject or AudioSceneDescriptors (from
//  AoeAim.Materialize / AseAim.Materialize) and delivers every leaf
//  BasicAudioObject it contains, recursively, via an existing
//  IAudioDeliveryAim implementation - FileAudioDelivery, AplayAudioDelivery,
//  WinmmAudioDelivery for plain playback, or PannedAudioDelivery for basic
//  stereo panning. ASD does not know or care which backend that is; it only
//  walks the structure and calls DeliverAsync once per leaf.
//
//  ASD only reads. It never writes to the Repository (per the spec: ASD is
//  the one constituent AIM that only consumes, never produces, an Asset).
//
//  Every delivery call requires a listener PointOfView: you cannot deliver
//  an Object or a Scene without first having placed where/how it is being
//  listened from - enforced as a hard precondition, not an optional
//  parameter. If the injected backend implements ISpatialAudioDeliveryAim,
//  the position recorded on each scene-level placement (see AseAim) is
//  passed through too, so the backend can actually use it (e.g. pan).
//
//  TIME-AWARE DELIVERY: if a placement's AudioObjectSpaceTime.Time carries a
//  real start time (SimpleTimeData[0].StartTime, in seconds), delivery of
//  that entry is delayed - measured against real wall-clock elapsed time
//  since DeliverSceneAsync began - until that moment. An entry with no
//  timing set is delivered immediately, same as before this existed, so
//  nothing already-built changes behaviour. This gives literal "object A
//  plays 0-5s, object B plays 5-10s" sequencing when the caller sets it.
// ---------------------------------------------------------------------------
public sealed class AsdAim
{
    private readonly IAudioDeliveryAim delivery;
    private readonly ISpatialAudioDeliveryAim? spatialDelivery;

    public AsdAim(IAudioDeliveryAim delivery)
    {
        this.delivery = delivery;
        spatialDelivery = delivery as ISpatialAudioDeliveryAim;
    }

    public async Task DeliverSceneAsync(AudioSceneDescriptors scene, PointOfView listenerPointOfView)
    {
        RequireListener(listenerPointOfView);
        var clock = Stopwatch.StartNew();
        await DeliverSceneInternalAsync(scene, listenerPointOfView, clock);
    }

    public async Task DeliverObjectAsync(AudioObject audioObject, PointOfView listenerPointOfView)
    {
        RequireListener(listenerPointOfView);
        await DeliverObjectInternalAsync(audioObject, listenerPointOfView, objectPosition: null);
    }

    private static void RequireListener(PointOfView listenerPointOfView)
    {
        if (listenerPointOfView is null)
        {
            throw new ArgumentNullException(
                nameof(listenerPointOfView),
                "A listener Point of View must be placed before an Object or Scene can be delivered.");
        }
    }

    private async Task DeliverSceneInternalAsync(AudioSceneDescriptors scene, PointOfView listenerPointOfView, Stopwatch clock)
    {
        foreach (var entry in scene.AudioObjects ?? new List<AudioSceneObjectEntry>())
        {
            if (entry.ObjectIDOrObject != null)
            {
                await WaitForStartTime(entry.AudioObjectSpaceTime, clock);

                // The position recorded on THIS scene-level placement is
                // applied uniformly to every leaf BAO under the object - see
                // AseAim's note: per-object placements inside AOE's own
                // composition (BasicAudioObjectEntry etc.) don't carry a
                // confirmed-correct position field yet, so finer-grained
                // per-BAO positioning within a composite object isn't wired.
                var objectPosition = entry.AudioObjectSpaceTime?.SpatialAttitude1;
                await DeliverObjectInternalAsync(entry.ObjectIDOrObject, listenerPointOfView, objectPosition);
            }
        }

        foreach (var subEntry in scene.SubAudioScenes ?? new List<SubAudioSceneEntry>())
        {
            if (subEntry.SubAudioSceneIDOrSubAudioScene != null)
            {
                await DeliverSceneInternalAsync(subEntry.SubAudioSceneIDOrSubAudioScene, listenerPointOfView, clock);
            }
        }
    }

    // Delays until (StartTime seconds) have elapsed on the scene's clock, if
    // a start time is actually present. No timing set - no delay, delivered
    // as soon as its turn in the walk comes up, exactly as before this
    // feature existed.
    private static async Task WaitForStartTime(SpaceTime? placement, Stopwatch clock)
    {
        var startTime = placement?.Time?.SimpleTimeData?.FirstOrDefault()?.StartTime;
        if (startTime is null) return;

        var delaySeconds = startTime.Value - clock.Elapsed.TotalSeconds;
        if (delaySeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        }
    }

    private async Task DeliverObjectInternalAsync(AudioObject audioObject, PointOfView listenerPointOfView, SpatialAttitude? objectPosition)
    {
        foreach (var basicEntry in audioObject.BasicAudioObjects ?? new List<BasicAudioObjectEntry>())
        {
            if (basicEntry.BAObjectIDOrBAObject != null)
            {
                if (spatialDelivery != null)
                {
                    await spatialDelivery.DeliverAsync(basicEntry.BAObjectIDOrBAObject, objectPosition, listenerPointOfView);
                }
                else
                {
                    await delivery.DeliverAsync(basicEntry.BAObjectIDOrBAObject);
                }
            }
        }

        foreach (var subEntry in audioObject.SubAudioObjects ?? new List<SubAudioObjectEntry>())
        {
            if (subEntry.SubAObjectIDOrSubAObject != null)
            {
                await DeliverObjectInternalAsync(subEntry.SubAObjectIDOrSubAObject, listenerPointOfView, objectPosition);
            }
        }
    }
}