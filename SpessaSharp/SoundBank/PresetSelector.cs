using System.Diagnostics;
using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;

namespace SpessaSharp.SoundBank;

internal static class PresetSelector
{
    private static BasicPreset GetAnyDrums(
        List<BasicPreset> presets, bool preferXG)
    {
        BasicPreset? any = null;
        if (preferXG) // Get any XG drums
        {
            foreach (var preset in presets)
            {
                if (preset.IsXGDrum) return preset;
                if (any == null && preset.IsDrum) any = preset;
            }
        }
        else // Get any GM/GS drums
        {
            foreach (var preset in presets)
            {
                if (preset.IsGMGSDrum) return preset;
                if (any == null && preset.IsDrum) any = preset;
            }
        }

        // Return any drum preset
        return any ?? /* ...no? */ presets[0]; // Then just return any preset
    }
    
    /// <summary>
    /// A sophisticated preset selection system based on the MIDI Patch system.
    /// This is the algorithm that the synthesizer uses for selecting presets.
    /// </summary>
    /// <param name="presets">The preset list.</param>
    /// <param name="patch">The patch to select.</param>
    /// <param name="system">The MIDI system to select for.</param>
    /// <returns>The selected patch.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Empty presets list</exception>
    public static BasicPreset Of(
        List<BasicPreset> presets,
        MidiPatch patch,
        Midi.System system)
    {
        ArgumentOutOfRangeException.ThrowIfZero(presets.Count, "No Presets!");

        if (patch.IsGMGSDrum && BankSelectHacks.IsSystemXG(system))
        {
            // GM/GS drums with XG. This shouldn't happen. Force XG drums.
            patch = patch with
            {
                IsGMGSDrum = false,
                BankLSB = 0,
                BankMSB = BankSelectHacks.GetDrumBank(system),
            };
        }

        var (program, bankMSB, bankLSB, isGMGSDrum) = patch;
        var isXG = BankSelectHacks.IsSystemXG(system);
        var xgDrums = isXG && BankSelectHacks.IsXGDrum(bankMSB);

        // Check for exact match
        foreach (var preset in presets)
        {
            if (!preset.Matches(patch)) continue;
            
            // Special case:
            // Non XG banks sometimes specify melodic "MT" presets at bank 127,
            // Which matches XG banks.
            // Testcase: 4gmgsmt-sf2_04-compat.sf2
            // Only match if the preset declares itself as drums
            if (!xgDrums || (xgDrums && preset.IsXGDrum))
                return preset;
            break;
        }

        // No exact match...
        if (isGMGSDrum) 
        {
            // GM/GS drums: check for the exact program match
            // pick the first drum preset, preferring GM/GS
            return SelectDrum(p => p.IsGMGSDrum, false);
        }
        
        if (xgDrums) 
        {
            // XG drums: Look for exact bank and program match
            // Pick any drums, preferring XG
            return SelectDrum(p => p.IsXGDrum, true);
        }
        
        // Melodic preset
        BasicPreset? specialXGCase = null;
        BasicPreset? firstMatch = null;
        foreach (var preset in presets)
        {
            if (!(preset.Program == patch.Program && !preset.IsDrum))
                continue;

            firstMatch ??= preset;

            // XG uses LSB so search for that.
            if (isXG && preset.BankLSB == bankLSB)
                return ReturnReplacement(preset);
            // GS uses MSB so search for that.
            if (!isXG && preset.BankMSB == bankMSB)
                return ReturnReplacement(preset);

            // Special XG case: 64 on LSB can't default to 64 MSB.
            // Testcase: Cybergate.mid
            // Selects 64 LSB on warm pad, on DLSbyXG.dls it gets replaced with Bird 2 SFX
            if (specialXGCase == null && (bankLSB != 64 || !isXG))
            {
                var bank = Math.Max(bankMSB, bankLSB);
                if (preset.BankLSB == bank || preset.BankMSB == bank)
                    specialXGCase = preset;
            }
        }
        
        // The first (matching program) preset
        return ReturnReplacement(
            specialXGCase ?? firstMatch ?? presets[0]);
        
        // Helper to log failed exact matches
        BasicPreset ReturnReplacement(BasicPreset preset)
        {
            Debug.WriteLine($"[WARN] Preset {
                patch.ToMidiString()} not found. ({
                    system}) Replaced with {preset}");
            return preset;
        }

        BasicPreset SelectDrum(
            Func<BasicPreset, bool> matches, bool preferXG)
        {
            BasicPreset? any = null;
            foreach (var preset in presets)
            {
                if (preset.Program != program)
                    continue;
                
                if (matches(preset))
                    return ReturnReplacement(preset);
                
                if (any == null && preset.IsDrum)
                    any = preset;
            }

            // No match, pick any matching drum
            if (any != null)
            {
                if (!preferXG || any.Program < 49)
                    // Program 49 and above start to diverge between GS and XG.
                    // For example,
                    // XG MU2000 and similar have regular drums on program 56, while GS has the SFX kit.
                    // So avoid selecting it and pick any XG drums.
                    return ReturnReplacement(any);
            }

            // Pick any drums
            var p = GetAnyDrums(presets, preferXG);
            return ReturnReplacement(p);
        }
    }
}