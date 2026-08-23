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

    // A backend that plays a scene AS A SCENE - everything at once, mixed -
    // rather than acting on each leaf as it arrives.
    private readonly IMixingAudioDeliveryAim? mixingDelivery;

    public AsdAim(IAudioDeliveryAim delivery)
    {
        this.delivery = delivery;
        spatialDelivery = delivery as ISpatialAudioDeliveryAim;
        mixingDelivery = delivery as IMixingAudioDeliveryAim;
    }

    public async Task DeliverSceneAsync(AudioSceneDescriptors scene, PointOfView listenerPointOfView)
    {
        RequireListener(listenerPointOfView);

        mixingDelivery?.Begin();

        var clock = Stopwatch.StartNew();
        await DeliverSceneInternalAsync(scene, listenerPointOfView, clock);

        // Every leaf has been handed over, which is the only point at which the
        // whole of it is known.
        if (mixingDelivery is not null) await mixingDelivery.FinishAsync();
    }

    public async Task DeliverObjectAsync(AudioObject audioObject, PointOfView listenerPointOfView)
    {
        RequireListener(listenerPointOfView);

        mixingDelivery?.Begin();

        await DeliverObjectInternalAsync(audioObject, listenerPointOfView, placement: null);

        if (mixingDelivery is not null) await mixingDelivery.FinishAsync();
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
                // The note here said per-component placements inside a
                // composition were not wired, because the position field was
                // not confirmed correct. It is, and they are: the whole
                // placement goes down, and each level adds its own.
                await DeliverObjectInternalAsync(
                    entry.ObjectIDOrObject, listenerPointOfView, entry.AudioObjectSpaceTime);
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
    private async Task WaitForStartTime(SpaceTime? placement, Stopwatch clock)
    {
        // A MIXING BACKEND IS NOT DELAYED. Waiting between leaves is what makes
        // a sequential backend play them in order; a backend that mixes wants
        // the offsets instead, so that two Objects starting at the same moment
        // actually start at the same moment. It is given the placement and
        // applies the times itself.
        if (mixingDelivery is not null) return;

        var startTime = placement?.Time?.SimpleTimeData?.FirstOrDefault()?.StartTime;
        if (startTime is null) return;

        var delaySeconds = startTime.Value - clock.Elapsed.TotalSeconds;
        if (delaySeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        }
    }

    private async Task DeliverObjectInternalAsync(AudioObject audioObject, PointOfView listenerPointOfView, SpaceTime? placement)
    {
        foreach (var basicEntry in audioObject.BasicAudioObjects ?? new List<BasicAudioObjectEntry>())
        {
            if (basicEntry.BAObjectIDOrBAObject != null)
            {
                if (spatialDelivery != null)
                {
                    // THE COMPONENT'S OWN PLACEMENT, offset by the container's.
                    //
                    // This passed the CONTAINER'S position for every component,
                    // so an Object of four voices panned all four identically -
                    // which is to say the arrangement inside an Object never
                    // reached the sound at all.
                    await spatialDelivery.DeliverAsync(
                        basicEntry.BAObjectIDOrBAObject,
                        Combine(placement, basicEntry.BasicAudioObjectSpaceTime),
                        listenerPointOfView);
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
                await DeliverObjectInternalAsync(
                    subEntry.SubAObjectIDOrSubAObject,
                    listenerPointOfView,
                    Combine(placement, subEntry.SubAudioObjectSpaceTime));
            }
        }
    }

    // Positions accumulate: a component's placement is within its immediate
    // container, so where it actually sounds is that offset added to everything
    // above it - the same rule the canvas draws by.
    //
    // Times accumulate too: a component starting two seconds into an Object that
    // itself starts at five begins at seven.
    private static SpaceTime? Combine(SpaceTime? outer, SpaceTime? inner)
    {
        if (outer is null) return inner;
        if (inner is null) return outer;

        var outerPosition = outer.SpatialAttitude1?.Position?.CartPosition;
        var innerPosition = inner.SpatialAttitude1?.Position?.CartPosition;

        var position = new double[3];
        for (var axis = 0; axis < 3; axis++)
        {
            position[axis] =
                (outerPosition is { Length: 3 } o ? o[axis] : 0) +
                (innerPosition is { Length: 3 } i ? i[axis] : 0);
        }

        var outerStart = outer.Time?.SimpleTimeData?.FirstOrDefault()?.StartTime ?? 0;
        var innerSegment = inner.Time?.SimpleTimeData?.FirstOrDefault();

        return new SpaceTime
        {
            SpatialAttitude1 = new SpatialAttitude
            {
                ObjectSpatialAttitudeID = Guid.NewGuid().ToString(),
                Position = new Position
                {
                    PositionID = Guid.NewGuid().ToString(),
                    CartPosition = position
                },
                Orientation = inner.SpatialAttitude1?.Orientation
                              ?? outer.SpatialAttitude1?.Orientation
                              ?? new Orientation()
            },
            Time = innerSegment is null ? outer.Time : new SimpleTime
            {
                SimpleTimeData = new List<TimeSegment>
                {
                    new TimeSegment
                    {
                        FlagsByte = innerSegment.FlagsByte,
                        StartTime = outerStart + innerSegment.StartTime,
                        EndTime   = outerStart + innerSegment.EndTime,
                        TimeType  = innerSegment.TimeType,

                        // TimeUnit is nullable on the segment and not on what is
                        // built from it, so an absent unit becomes seconds -
                        // which is what "00" means and what every caller uses.
                        TimeUnit  = innerSegment.TimeUnit ?? "00"
                    }
                }
            }
        };
    }
}