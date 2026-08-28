using System;
using System.IO;

using Mpai.Core;
using Mpai.Paf.Fir;
using Mpai.Mmc.Sir;

namespace Mpai.Hci.Idr;

// Media-taking enrolment for the SubjectGallery. The gallery itself is model-
// agnostic (Mpai.Core, stores embeddings + cosine), so the convenience of
// enrolling FROM a photo / wav lives here, where the FIR (ArcFace) and SIR
// (ECAPA) embedders are available. This is the "add a user entry" entry point:
// give a face image and/or a voice clip + a subject id, and the person becomes a
// single entry that FIR, SIR and IDR all recognise.
public static class SubjectEnrolment
{
    // Enrol / update a subject from a face image and/or a voice clip. Either may
    // be null (partial enrolment). Runs the embedders and stores the embeddings
    // in the gallery under one subject id.
    public static void EnrolSubject(
        SubjectGallery gallery,
        string subjectId,
        ArcFaceRecogniser? faceRecogniser = null,
        string? faceImagePath = null,
        SpeakerEmbedder? speakerEmbedder = null,
        string? voiceClipPath = null)
    {
        float[]? face = null, voice = null;

        if (faceImagePath is not null)
        {
            if (faceRecogniser is null) throw new ArgumentNullException(nameof(faceRecogniser),
                "a face image was given but no ArcFaceRecogniser to embed it");
            face = faceRecogniser.Embed(File.ReadAllBytes(faceImagePath));
        }
        if (voiceClipPath is not null)
        {
            if (speakerEmbedder is null) throw new ArgumentNullException(nameof(speakerEmbedder),
                "a voice clip was given but no SpeakerEmbedder to embed it");
            voice = speakerEmbedder.Embed(WavReader.ReadMono16k(voiceClipPath));
        }

        gallery.EnrolEmbeddings(subjectId, face, voice);
    }
}
