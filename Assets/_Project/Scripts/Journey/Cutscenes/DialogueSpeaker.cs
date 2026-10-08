using UnityEngine;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>Who says a line, from the cast table in Docs/Story.md.</summary>
    public enum DialogueSpeaker
    {
        FactoryOS,
        Pip,
        Unit047,
        Captain
    }

    /// <summary>The waveform of a speaker's voice blip.</summary>
    public enum BlipWave
    {
        Sine,
        Triangle,
        Square,
        Sawtooth
    }

    /// <summary>How each speaker looks and sounds in the subtitles: name tag, colour and voice blip.</summary>
    public static class DialogueSpeakers
    {
        /// <summary>The name tag shown before the speaker's lines.</summary>
        public static string Tag(DialogueSpeaker speaker)
        {
            switch (speaker)
            {
                case DialogueSpeaker.Pip: return "PIP, maintenance radio";
                case DialogueSpeaker.Unit047: return "UNIT 047";
                case DialogueSpeaker.Captain: return "CAPTAIN BOT";
                default: return "FACTORY OS";
            }
        }

        /// <summary>The name tag's colour.</summary>
        public static Color Colour(DialogueSpeaker speaker)
        {
            switch (speaker)
            {
                case DialogueSpeaker.Pip: return Hex(0x3D, 0xDB, 0xB0);      // mint
                case DialogueSpeaker.Unit047: return Hex(0x62, 0xD8, 0xFF);  // cyan
                case DialogueSpeaker.Captain: return Hex(0xFF, 0x9F, 0x1C);  // orange
                default: return Hex(0xFF, 0x5A, 0x4E);                       // tomato
            }
        }

        /// <summary>The voice blip's waveform: OS square, Pip triangle, Captain sawtooth, 047 sine.</summary>
        public static BlipWave Wave(DialogueSpeaker speaker)
        {
            switch (speaker)
            {
                case DialogueSpeaker.Pip: return BlipWave.Triangle;
                case DialogueSpeaker.Unit047: return BlipWave.Sine;
                case DialogueSpeaker.Captain: return BlipWave.Sawtooth;
                default: return BlipWave.Square;
            }
        }

        /// <summary>The voice blip's base pitch in Hz: Factory OS low, Pip high.</summary>
        public static float Pitch(DialogueSpeaker speaker)
        {
            switch (speaker)
            {
                case DialogueSpeaker.Pip: return 880f;
                case DialogueSpeaker.Unit047: return 520f;
                case DialogueSpeaker.Captain: return 220f;
                default: return 140f;
            }
        }

        static Color Hex(byte r, byte g, byte b) => new Color32(r, g, b, 255);
    }
}
