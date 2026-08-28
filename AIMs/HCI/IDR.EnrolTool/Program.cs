using System;
using System.IO;

using Mpai.Core;
using Mpai.Paf.Fir;
using Mpai.Mmc.Sir;
using Mpai.Hci.Idr;

namespace Mpai.Hci.Idr.EnrolTool;

// Interactive enrolment tool: asks for a subject name, a face image path, and a
// voice clip path, then embeds and stores the person as ONE entry in the shared
// SubjectGallery (persisted to JSON). Run it repeatedly to add users; the
// gallery is loaded, appended to, and saved each time.
//   arg 0: gallery JSON path (default D:\AI\TestData\gallery.json)
//   arg 1: models dir        (default D:\AI\Models)
public static class Program
{
    public static int Main(string[] args)
    {
        string galleryPath = args.Length >= 1 ? args[0] : @"D:\AI\TestData\gallery.json";
        string models      = args.Length >= 2 ? args[1] : @"D:\AI\Models";
        string arc   = Path.Combine(models, "glintr100.onnx");
        string ecapa = Path.Combine(models, "ecapa-tdnn.onnx");
        string scrfd = Path.Combine(models, "scrfd_10g_bnkps.onnx");

        if (!File.Exists(arc))   { Console.WriteLine($"Missing face model: {arc}"); return 1; }
        if (!File.Exists(ecapa)) { Console.WriteLine($"Missing voice model: {ecapa}"); return 1; }
        if (!File.Exists(scrfd)) { Console.WriteLine($"Missing face detector: {scrfd}"); return 1; }

        var gallery = SubjectGallery.Load(galleryPath);
        Console.WriteLine($"Gallery: {galleryPath}");
        Console.WriteLine($"Currently enrolled ({gallery.Count}): {(gallery.Count == 0 ? "(none)" : string.Join(", ", gallery.SubjectIds))}");
        Console.WriteLine();

        using var face     = new ArcFaceRecogniser(arc);
        using var voice    = new SpeakerEmbedder(ecapa);
        using var detector = new Mpai.Osd.VisualScene.ScrfdFaceDetector(scrfd);

        while (true)
        {
            Console.Write("Subject name (blank to finish): ");
            string? name = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(name)) break;

            Console.Write("Face image path (blank to skip): ");
            string? img = Console.ReadLine()?.Trim().Trim('"');
            if (img == "") img = null;
            if (img != null && !File.Exists(img)) { Console.WriteLine($"  ! not found: {img} - skipping face."); img = null; }

            Console.Write("Voice clip .wav path (blank to skip): ");
            string? wav = Console.ReadLine()?.Trim().Trim('"');
            if (wav == "") wav = null;
            if (wav != null && !File.Exists(wav)) { Console.WriteLine($"  ! not found: {wav} - skipping voice."); wav = null; }

            if (img == null && wav == null)
            {
                Console.WriteLine("  Nothing to enrol (no face, no voice). Skipped.");
                Console.WriteLine();
                continue;
            }

            try
            {
                SubjectEnrolment.EnrolSubject(gallery, name,
                    faceRecogniser: face, faceImagePath: img,
                    speakerEmbedder: voice, voiceClipPath: wav,
                    faceDetector: detector);
                gallery.Save(galleryPath);
                string got = (img != null ? "face" : "") + (img != null && wav != null ? "+" : "") + (wav != null ? "voice" : "");
                Console.WriteLine($"  Enrolled '{name}' ({got}). Saved. Gallery now has {gallery.Count} subject(s).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ! enrolment failed: {ex.Message}");
            }
            Console.WriteLine();
        }

        Console.WriteLine($"Done. {gallery.Count} subject(s) enrolled: {string.Join(", ", gallery.SubjectIds)}");
        return 0;
    }
}
