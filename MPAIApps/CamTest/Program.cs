using System;
using OpenCvSharp;

// CamTest - probe which camera index (and backend) actually delivers a frame.
// Zoom shows video, but our VideoCapture(0) returns instantly - so index 0 may be
// a virtual/wrong camera, or the default (MSMF) backend can't grab from this one.
// This tries indices 0..3 on the default backend and on DirectShow, and reports
// which OPEN and which return a NON-EMPTY frame.
internal static class CamTest
{
    private static void Main()
    {
        Console.WriteLine("CamTest - probing camera indices 0..3 (default + DirectShow)\n");
        Probe("default", i => new VideoCapture(i));
        Probe("DirectShow", i => new VideoCapture(i, VideoCaptureAPIs.DSHOW));
        Console.WriteLine("\nDone. The index+backend that shows 'FRAME ok WxH' is your real camera.");
        Console.WriteLine("Press Enter to close.");
        Console.ReadLine();
    }

    private static void Probe(string backend, Func<int, VideoCapture> open)
    {
        for (int i = 0; i < 4; i++)
        {
            try
            {
                using var cap = open(i);
                if (!cap.IsOpened()) { Console.WriteLine($"[{backend}] index {i}: NOT opened"); continue; }
                using var frame = new Mat();
                for (int w = 0; w < 5; w++) cap.Read(frame);   // warm-up
                cap.Read(frame);
                if (frame.Empty()) Console.WriteLine($"[{backend}] index {i}: opened, EMPTY frame");
                else               Console.WriteLine($"[{backend}] index {i}: FRAME ok {frame.Width}x{frame.Height}");
            }
            catch (Exception ex) { Console.WriteLine($"[{backend}] index {i}: ERROR {ex.Message}"); }
        }
    }
}
