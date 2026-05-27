using System.Diagnostics;
using System.Runtime.InteropServices;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.DLS;

internal sealed class DLSArticulation
{
    public enum Mode { DLS1, DLS2 }

    private List<ConnectionBlock>? _connectionBlocks;
    public Mode DMode = Mode.DLS2;
    
    public int Len => _connectionBlocks?.Count ?? 0;

    public void CopyFrom(DLSArticulation input)
    {
        DMode = input.DMode;
        _connectionBlocks?.Clear();
        if (input.Len == 0) return;

        _connectionBlocks ??= new List<ConnectionBlock>(input.Len);
        _connectionBlocks.AddRange(input._connectionBlocks!);
    }

    public void Add(ConnectionBlock block)
    {
        _connectionBlocks ??= new List<ConnectionBlock>(20);
        _connectionBlocks.Add(block);
    }

    public void FromSFZone(BasicInstrument.Zone z)
    {
        DMode = Mode.DLS2;
        
        // Copy to avoid changing the input zone
        var zone = new BasicZone();
        zone.CopyFrom(z.Basic);
        
        // Read_articulation.ts:
        // According to viena and another strange (with modulators) rendition of gm.dls in sf2,
        // It shall be divided by -128,
        // And a strange correction needs to be applied to the real value:
        // Real + (60 / 128) * scale
        // We do this here.
        foreach (var relativeGenerator in zone.Generators)
        {
            var absoluteCounterPart = Generator.Type.Invalid;

            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (relativeGenerator.GType)
            {
                case Generator.Type.KeyNumToVolEnvDecay:
                    absoluteCounterPart = Generator.Type.DecayVolEnv;
                    break;
                case Generator.Type.KeyNumToVolEnvHold:
                    absoluteCounterPart = Generator.Type.HoldVolEnv;
                    break;
                case Generator.Type.KeyNumToModEnvDecay:
                    absoluteCounterPart = Generator.Type.DecayModEnv;
                    break;
                case Generator.Type.KeyNumToModEnvHold:
                    absoluteCounterPart = Generator.Type.HoldModEnv;
                    break;
                default:
                    continue;
            }

            var optAbsValue = zone.GetGenerator(absoluteCounterPart);
            var dlsRelative = relativeGenerator.Value * -128d;

            if (optAbsValue is not {} absoluteValue)
                // There's no absolute generator here.
                continue;

            var subtraction = (60d / 128d) * dlsRelative;
            var newAbsolute = Util.Round(absoluteValue - subtraction);

            zone.SetGenerator(relativeGenerator.GType, 
                Util.Round(dlsRelative), false);
            zone.SetGenerator(absoluteCounterPart, newAbsolute, false);
        }

        foreach (var generator in zone.Generators)
            ConnectionBlock.FromSFGenerator(generator, this);
        foreach (var modulator in zone.Modulators)
            ConnectionBlock.FromSFModulator(modulator, this);
    }

    /// <summary>Chunk list for the region/instrument (containing lar2 or lart)</summary>
    /// <param name="chunks"></param>
    public void Read(Span<RIFFChunk> chunks)
    {
        var optLart = RIFFChunk.FindListType(chunks, new RIFFChunk.FourCC("lart"));
        var optLar2 = RIFFChunk.FindListType(chunks, new RIFFChunk.FourCC("lar2"));

        if (optLart is { } lart)
        {
            DMode = Mode.DLS1;
            var data = lart.Data;
            while (data.Count > 0)
            {
                var chunk = RIFFChunk.Read(ref data);
                // Note:
                // DLS Specification says that lar2 should only have art2, but a DirectMusic Producer example
                // "FarmGame.dls" has 'art1' in there.
                // Hence, we allow art2 in lart and art1 in lar2.
                if (!(chunk.Header == "art1" || chunk.Header == "art2"))
                    // There may be a cdl chunk, testcase romania_main.dls
                    continue;
                var artData = chunk.Data;
                var cbSize = Util.ReadLittleEndian(ref artData, 4);
                if (cbSize != 8)
                    Debug.WriteLine(
                        $"CbSize in articulation mismatch. Expected 8, got {
                            cbSize}");
                
                var connectionsAmount = Util.ReadLittleEndian(ref artData, 4);
                for (var _ = 0; _ < connectionsAmount; _++)
                    Add(ConnectionBlock.Read(ref artData));
            }
        }
        else if (optLar2 is { } lar2)
        {
            DMode = Mode.DLS2;
            var data = lar2.Data;
            while (data.Count > 0)
            {
                var chunk = RIFFChunk.Read(ref data);
                // Note:
                // DLS Specification says that lar2 should only have art2, but a DirectMusic Producer example
                // "FarmGame.dls" has 'art1' in there.
                // Hence, we allow art2 in lart and art1 in lar2.
                if (!(chunk.Header == "art1" || chunk.Header == "art2"))
                    // There may be a cdl chunk, testcase romania_main.dls
                    continue;
                
                var artData = chunk.Data;
                var cbSize = Util.ReadLittleEndian(ref artData, 4);
                if (cbSize != 8)
                    Debug.WriteLine(
                        $"CbSize in articulation mismatch. Expected 8, got {
                            cbSize}");
                
                var connectionsAmount = Util.ReadLittleEndian(ref artData, 4);
                for (var _ = 0; _ < connectionsAmount; _++)
                    Add(ConnectionBlock.Read(ref artData));
            }
        }
    }
    
    /// <summary>Note: this writes "lar2", not just "art2"</summary>
    /// <returns></returns>
    public ArraySegment<byte> Write()
    {
        var art2Data = new byte[8];
        var art2Seg = (ArraySegment<byte>)art2Data;
        
        Util.WriteDword(ref art2Seg, 8); // CbSize
        Util.WriteDword(ref art2Seg, Len); // CConnectionBlocks

        var output = _connectionBlocks?.Select(
            a => a.Write()).ToList() ?? [];
        output.Insert(0, art2Data);

        var art2 = RIFFChunk.WriteParts(
            new RIFFChunk.FourCC(DMode == Mode.DLS2 ? "art2" : "art1"),
            CollectionsMarshal.AsSpan(output));

        return RIFFChunk.Write(
            new RIFFChunk.FourCC(DMode == Mode.DLS2 ? "lar2" : "lart"),
            art2, false, true);
    }

    /// <summary>Converts DLS articulation into an SF zone.</summary>
    /// <param name="zone">The zone to write to.</param>
    public void ToSFZone(BasicZone zone)
    {
        if (_connectionBlocks != null) foreach (var connection in _connectionBlocks)
        {
            // if source and control are both zero, it's a generator
            if (connection.IsStaticParameter)
            {
                connection.ToSFGenerator(zone);
                continue;
            }
            
            // SF2 uses 16-bit amounts, DLS uses 32-bit scale.
            var amount = connection.ShortScale;
            var control = connection.Control.Source;
            
            // A few special cases which are generators
            if (control == ConnectionSource.DLSSource.None)
            {
                var source = connection.Source.Source;
                var destination = connection.Destination;
                
                // The keyNum source
                // It usually requires a special treatment
                if (source == ConnectionSource.DLSSource.KeyNum)
                {
                    // Scale tuning
                    if (destination == ConnectionSource.DLSDestination.Pitch)
                    {
                        zone.SetGenerator(
                            Generator.Type.ScaleTuning,
                            Util.Round(amount / 128d));
                        continue;
                    }

                    if (destination.IsAny(
                        ConnectionSource.DLSDestination.ModEnvHold,
                        ConnectionSource.DLSDestination.ModEnvDecay,
                        ConnectionSource.DLSDestination.VolEnvHold,
                        ConnectionSource.DLSDestination.VolEnvDecay))
                    {
                        // Skip, will be applied later
                        continue;
                    }
                }
                else if (connection.ToCombinedSFDestination() is { } specialGen)
                {
                    zone.SetGenerator(specialGen, amount);
                    continue;
                }
            }
            
            // Modulator, transform!
            connection.ToSFModulator(zone);
        }
        
        // Perform correction for the key to something generators
        if (_connectionBlocks != null) foreach (var connection in _connectionBlocks)
        {
            if (connection.Source.Source != ConnectionSource.DLSSource.KeyNum)
                continue;

            var generatorAmount = connection.ShortScale;
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            var d = connection.Destination;
            switch (d)
            {
                default:
                case var _ when d == ConnectionSource.DLSDestination.VolEnvHold:
                    // Key to vol env hold
                    ApplyKeyToCorrection(
                        generatorAmount,
                        Generator.Type.KeyNumToVolEnvHold,
                        Generator.Type.HoldVolEnv,
                        ConnectionSource.DLSDestination.VolEnvHold);
                    break;
                
                case var _ when d == ConnectionSource.DLSDestination.VolEnvDecay:
                    ApplyKeyToCorrection(
                        generatorAmount, 
                        Generator.Type.KeyNumToVolEnvDecay,
                        Generator.Type.DecayVolEnv,
                        ConnectionSource.DLSDestination.VolEnvDecay);
                    break;
                
                case var _ when d == ConnectionSource.DLSDestination.ModEnvHold:
                    ApplyKeyToCorrection(
                        generatorAmount, 
                        Generator.Type.KeyNumToModEnvHold,
                        Generator.Type.HoldModEnv,
                        ConnectionSource.DLSDestination.ModEnvHold);
                    break;
                
                case var _ when d == ConnectionSource.DLSDestination.ModEnvDecay:
                    ApplyKeyToCorrection(
                        generatorAmount, 
                        Generator.Type.KeyNumToModEnvDecay,
                        Generator.Type.DecayModEnv,
                        ConnectionSource.DLSDestination.ModEnvDecay);
                    break;
            }
        }
        
        // Perform DLS1 corrections
        if (DMode == Mode.DLS1)
        {
            // DLS1 only has modulation LFO.
            // Copy the parameters to vib LFO and convert all pitch values to vibrato LFO (including mod wheel modulator)
            // This ensures that it stays in sync when using things like GS controller matrix

            // Copy over delay and rate to vibrato LFO
            zone.SetGenerator(
                Generator.Type.DelayVibLFO,
                zone.GetGenerator(Generator.Type.DelayModLFO));

            zone.SetGenerator(
                Generator.Type.FreqVibLFO,
                zone.GetGenerator(Generator.Type.FreqModLFO));

            // Convert pitch excursion to vibrato LFO
            zone.SetGenerator(
                Generator.Type.VibLFOToPitch,
                zone.GetGenerator(Generator.Type.ModLFOToPitch));
                
            zone.SetGenerator(Generator.Type.ModLFOToPitch, null);

            foreach (ref var mod in
                CollectionsMarshal.AsSpan(zone.Modulators)) 
            {
                if (mod.Destination == Generator.Type.ModLFOToPitch)
                    mod = mod with { Destination = Generator.Type.VibLFOToPitch };
            }
        }
        
        return;

        void ApplyKeyToCorrection(
            int value,
            Generator.Type keyToGen,
            Generator.Type realGen,
            ConnectionSource.DLSDestination dlsDestination)
        {
            VerifyKeyToEnv(keyToGen);
            
            // According to viena and another strange (with modulators) rendition of gm.dls in sf2,
            // It shall be divided by -128
            // And a strange correction needs to be applied to the real (generator) value:
            // Real + (60 / 128) * scale
            // Where real means the actual generator (e.g. decayVolEnv
            // And scale means the keyNumToVolEnvDecay
            var keyToGenValue = Util.Round(value / -128d);
            zone.SetGenerator(keyToGen, keyToGenValue);
            
            // Airfont 340 fix
            if (keyToGenValue > 120) return;

            // Apply correction
            foreach (var block in _connectionBlocks!)
            {
                if (!(block.IsStaticParameter &&
                      block.Destination == dlsDestination))
                    continue;
            
                // Overwrite existing
                var correction = (60d / 128d * value);
                var newValue = Util.Round(correction + block.ShortScale);

                zone.SetGenerator(realGen, newValue);
                break;
            }
        }
    }

    private static void VerifyKeyToEnv(Generator.Type type)
    {
        if (type is
            Generator.Type.KeyNumToModEnvDecay or
            Generator.Type.KeyNumToModEnvHold  or
            Generator.Type.KeyNumToVolEnvDecay or
            Generator.Type.KeyNumToVolEnvHold) return;
        throw SpessaException.ParsingSoundBank(
            $"Invalid KeyToEnv type: {type}");
    }
}