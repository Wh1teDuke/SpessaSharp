namespace SpessaSharp.Utils;

public class SpessaException: Exception
{
    public interface IMidiException;
    public interface ISoundBankException;
    public interface ISynthesizerException;

    // Parsing
    public class Parser: SpessaException
    {
        public Parser(string msg): base(msg) {}
        public Parser(string msg, Exception inner): base(msg, inner) {}
    }

    public class ParserMidi: Parser, IMidiException
    {
        public ParserMidi(string msg): base(msg) {}
        public ParserMidi(Exception inner): base(
            "Error Parsing Midi", inner) {}
    }

    public class ParserSoundBank: Parser, ISoundBankException
    {
        public ParserSoundBank(string msg): base(msg) {}
        public ParserSoundBank(Exception inner): base(
            "Error Parsing Soundbank", inner) {}
    }

    public SpessaException(string msg): base(msg) {}
    public SpessaException(string msg, Exception inner): base(msg, inner) {}

    // Util
    public static InvalidOperationException Invalid(string msg) => new (msg);

    public static ParserMidi ParsingMidi(string msg) => new (msg);
    public static ParserMidi ParsingMidi(Exception inner) => new (inner);

    public static ParserSoundBank ParsingSoundBank(string msg) => new (msg);
    public static ParserSoundBank ParsingSoundBank(Exception inner) => new (inner);
}