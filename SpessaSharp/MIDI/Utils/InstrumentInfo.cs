namespace SpessaSharp.MIDI.Utils;

// https://web.archive.org/web/20211018235158/https://www.midi.org/specifications-old/item/gm-level-1-sound-set
public static class InstrumentInfo
{
    public enum Melodic
    {
        AcousticGrandPiano, BrightAcousticPiano, ElectricGrandPiano, 
        HonkyTonkPiano, ElectricPiano1, ElectricPiano2, Harpsichord,
        Clavi, Celesta, Glockenspiel, MusicBox, Vibraphone, Marimba,
        Xylophone, TubularBells, Dulcimer, DrawbarOrgan, PercussiveOrgan,
        RockOrgan, ChurchOrgan, ReedOrgan, Accordion, Harmonica,
        TangoAccordion, AcousticGuitarNylon, AcousticGuitarSteel,
        ElectricGuitarJazz, ElectricGuitarClean, ElectricGuitarMuted,
        OverdrivenGuitar, DistortionGuitar, GuitarHarmonics,
        AcousticBass, ElectricBassFinger, ElectricBassPick, FretlessBass,
        SlapBass1, SlapBass2, SynthBass1, SynthBass2, Violin, Viola,
        Cello, Contrabass, TremoloStrings, PizzicatoStrings,
        OrchestralHarp, Timpani, StringEnsemble1, StringEnsemble2,
        SynthStrings1, SynthStrings2, ChoirAahs, VoiceOohs,
        SynthVoice, OrchestraHit, Trumpet, Trombone, Tuba,
        MutedTrumpet, FrenchHorn, BrassSection, SynthBrass1, SynthBrass2,
        SopranoSax, AltoSax, TenorSax, BaritoneSax, Oboe, EnglishHorn,
        Bassoon, Clarinet, Piccolo, Flute, Recorder, PanFlute,
        BlownBottle, Shakuhachi, Whistle, Ocarina, Lead1_Square,
        Lead2_Sawtooth, Lead3_Calliope, Lead4_Chiff, Lead5_Charang,
        Lead6_Voice, Lead7_Fifths, Lead8_BassPlusLead, Pad1_NewAge,
        Pad2_Warm, Pad3_Polysynth, Pad4_Choir, Pad5_Bowed, Pad6_Metallic,
        Pad7_Halo, Pad8_Sweep, FX1_Rain, FX2_Soundtrack, FX3_Crystal,
        FX4_Atmosphere, FX5_Brightness, FX6_Goblins, FX7_Echoes,
        FX8_SciFi, Sitar, Banjo, Shamisen, Koto, Kalimba, BagPipe, Fiddle,
        Shanai, TinkleBell, Agogo, SteelDrums, Woodblock, TaikoDrum,
        MelodicTom, SynthDrum, ReverseCymbal, GuitarFretNoise, BreathNoise,
        Seashore, BirdTweet, TelephoneRing, Helicopter, Applause, Gunshot,
    }

    private static readonly Melodic[] MelodicVal = Enum.GetValues<Melodic>();

    private static readonly string[] MelodicStr =
    [
        "Acoustic Grand Piano", "Bright Acoustic Piano", 
        "Electric Grand Piano", "Honky-tonk Piano", "Electric Piano 1",
        "Electric Piano 2", "Harpsichord", "Clavi", "Celesta", "Glockenspiel",
        "Music Box", "Vibraphone", "Marimba", "Xylophone", "Tubular Bells",
        "Dulcimer", "Drawbar Organ", "Percussive Organ", "Rock Organ",
        "Church Organ", "Reed Organ", "Accordion", "Harmonica",
        "Tango Accordion", "Acoustic Guitar (nylon)",
        "Acoustic Guitar (steel)", "Electric Guitar (jazz)",
        "Electric Guitar (clean)", "Electric Guitar (muted)",
        "Overdriven Guitar", "Distortion Guitar", "Guitar harmonics",
        "Acoustic Bass", "Electric Bass (finger)", "Electric Bass (pick)",
        "Fretless Bass", "Slap Bass 1", "Slap Bass 2", "Synth Bass 1",
        "Synth Bass 2", "Violin", "Viola", "Cello", "Contrabass",
        "Tremolo Strings", "Pizzicato Strings", "Orchestral Harp", "Timpani",
        "String Ensemble 1", "String Ensemble 2", "SynthStrings 1",
        "SynthStrings 2", "Choir Aahs", "Voice Oohs", "Synth Voice",
        "Orchestra Hit", "Trumpet", "Trombone", "Tuba", "Muted Trumpet",
        "French Horn", "Brass Section", "SynthBrass 1", "SynthBrass 2",
        "Soprano Sax", "Alto Sax", "Tenor Sax", "Baritone Sax", "Oboe",
        "English Horn", "Bassoon", "Clarinet", "Piccolo", "Flute", "Recorder",
        "Pan Flute", "Blown Bottle", "Shakuhachi", "Whistle", "Ocarina",
        "Lead 1 (square)", "Lead 2 (sawtooth)", "Lead 3 (calliope)",
        "Lead 4 (chiff)", "Lead 5 (charang)", "Lead 6 (voice)", 
        "Lead 7 (fifths)", "Lead 8 (bass + lead)", "Pad 1 (new age)",
        "Pad 2 (warm)", "Pad 3 (polysynth)", "Pad 4 (choir)", "Pad 5 (bowed)",
        "Pad 6 (metallic)", "Pad 7 (halo)", "Pad 8 (sweep)", "FX 1 (rain)",
        "FX 2 (soundtrack)", "FX 3 (crystal)", "FX 4 (atmosphere)",
        "FX 5 (brightness)", "FX 6 (goblins)", "FX 7 (echoes)",
        "FX 8 (sci-fi)", "Sitar", "Banjo", "Shamisen", "Koto",
        "Kalimba", "Bag pipe", "Fiddle", "Shanai", "Tinkle Bell", "Agogo",
        "Steel Drums", "Woodblock", "Taiko Drum", "Melodic Tom", "Synth Drum",
        "Reverse Cymbal", "Guitar Fret Noise", "Breath Noise", "Seashore",
        "Bird Tweet", "Telephone Ring", "Helicopter", "Applause", "Gunshot",];

    public static string ToString(Melodic m) => MelodicStr[(int)m];

    public enum Drum
    {
        // Roland GS 25 -
        SnareRoll, FingerSnap, HighQ, Slap, ScratchPush, 
        ScratchPull, Sticks, SquareClick, MetronomeClick,
        MetronomeBell,
        // 
        AcousticBassDrum, BassDrum1, SideStick, AcousticSnare, HandClap,
        ElectricSnare, LowFloorTom, ClosedHiHat, HighFloorTom, PedalHiHat,
        LowTom, OpenHiHat, LowMidTom, HiMidTom, CrashCymbal1, HighTom,
        RideCymbal1, ChineseCymbal, RideBell, Tambourine, SplashCymbal,
        Cowbell, CrashCymbal2, Vibraslap, RideCymbal2, HiBongo,
        LowBongo, MuteHiConga, OpenHiConga, LowConga, HighTimbale,
        LowTimbale, HighAgogo, LowAgogo, Cabasa, Maracas, ShortWhistle,
        LongWhistle, ShortGuiro, LongGuiro, Claves, HiWoodBlock, LowWoodBlock,
        MuteCuica, OpenCuica, MuteTriangle, OpenTriangle,
        // Roland GS 82 -
        Shaker, JingleBell, Belltree, Castanets,
        MuteSurdo, OpenSurdo,
    }
    
    private static readonly Drum[] DrumVal = Enum.GetValues<Drum>();

    private static readonly string[] DrumStr =
    [
        // Roland GS 25 -
        "Snare Roll", "Finger Snap", "High Q", "Slap", "Scratch Push", 
        "Scratch Pull", "Sticks", "Square Click", "Metronome Click",
        "Metronome Bell",
        // 35 -
        "Acoustic Bass Drum", "Bass Drum 1", "Side Stick", "Acoustic Snare",
        "Hand Clap", "Electric Snare", "Low Floor Tom", "Closed Hi Hat",
        "High Floor Tom", "Pedal Hi-Hat", "Low Tom", "Open Hi-Hat",
        "Low-Mid Tom", "Hi-Mid Tom", "Crash Cymbal 1", "High Tom",
        "Ride Cymbal 1", "Chinese Cymbal", "Ride Bell", "Tambourine",
        "Splash Cymbal", "Cowbell", "Crash Cymbal 2", "Vibraslap",
        "Ride Cymbal 2", "Hi Bongo", "Low Bongo", "Mute Hi Conga",
        "Open Hi Conga", "Low Conga", "High Timbale", "Low Timbale",
        "High Agogo", "Low Agogo", "Cabasa", "Maracas",
        "Short Whistle", "Long Whistle", "Short Guiro", "Long Guiro",
        "Claves", "Hi Wood Block", "Low Wood Block", "Mute Cuica",
        "Open Cuica", "Mute Triangle", "Open Triangle",
        // Roland GS 82 -
        "Shaker", "Jingle Bell", "Belltree", "Castanets",
        "Mute Surdo", "Open Surdo",
    ];
    
    public static string ToString(Drum d) => DrumStr[(int)d];

    public enum Family
    {
        Piano, ChromaticPercussion, Organ, Guitar,
        Bass, Strings, Ensemble, Brass, Reed, Pipe,
        SynthLead, SynthPad, SynthEffects, Ethnic,
        Percussive, SoundEffects,
    }
    
    private static readonly Family[] FamilyVal = Enum.GetValues<Family>();

    private static readonly string[] FamilyStr =
    [
        "Piano", "Chromatic Percussion", "Organ",
        "Guitar", "Bass", "Strings", "Ensemble",
        "Brass", "Reed", "Pipe", "Synth Lead", "Synth Pad",
        "Synth Effects", "Ethnic", "Percussive", "Sound Effects",
    ];
    
    public static string ToString(Family f) => FamilyStr[(int)f];

    public static Family GetFamily(MidiPatch patch) => FamilyVal[patch.Program / 8];
    public static Family GetFamily(Melodic m) => FamilyVal[(int)m / 8];
    public static Melodic GetMelodic(MidiPatch patch) => MelodicVal[patch.Program];
    public static Drum? TryGetDrum(int key)
    {
        var i = key - 25;
        return i < 0 || i >= DrumVal.Length ? null : DrumVal[i];
    }
    public static int ToMidiNote(Drum d) => (int)d + 25;
}