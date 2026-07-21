using System.Diagnostics;
using System.Runtime.InteropServices;
using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.SoundBank;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine;

public sealed class SoundBankManager: BasePreset.IGetter<SynthPatch>
{
    /// <summary> </summary>
    /// <param name="ID">The unique string identifier of the sound bank.</param>
    /// <param name="SoundBank">The sound bank itself.</param>
    /// <param name="BankOffset">The bank MSB offset for this sound bank.</param>
    internal readonly record struct ListEntry(
        string ID,
        SoundBank.SoundBank SoundBank,
        int BankOffset);

    internal readonly UsedProgramsAndKeys.Cache Cache = new();
    
    /// <summary> All the sound banks, ordered from the most important to the least. </summary>
    internal readonly List<ListEntry> SoundBankList = new(8);

    /// <summary>
    /// The two GS user drum sets, available on programs 65 and 66.
    /// These override any sound bank presets at those program numbers.
    /// Note that these are not selectable in XG mode (implied by the bank selection system)
    /// </summary>
    internal readonly UserDrumSet[] UserDrumSets;
    private readonly Action _presetListChangeCallbank;
    
    private readonly List<SynthPatch> _selectablePresetList = new(400);
    private readonly List<MidiPatch.Full> _presetList = new(400);

    /// <summary> </summary>
    /// <param name="presetListChangeCallbank">Supplied by the parent synthesizer class, this is called whenever the preset list changes.</param>
    public SoundBankManager(Action presetListChangeCallbank)
    {
        _presetListChangeCallbank = presetListChangeCallbank;
        
        // Patch resolver for GS user drum
        UserDrumSet.ResolvePatch resolvePatch = patch =>
            GetPreset(patch, SystemGetter());

        UserDrumSets = [
            new UserDrumSet(Synthesizer.GS_USER_DRUM_1, "User Drum Set 1", resolvePatch),
            new UserDrumSet(Synthesizer.GS_USER_DRUM_2, "User Drum Set 2", resolvePatch),
        ];
    }

    public void ClearUsedKeysCache() => Cache.Clear();

    /// <summary>The list of all presets in the sound bank stack.</summary>
    public IReadOnlyList<MidiPatch.Full> PresetList => _presetList;

    /// <summary>The current sound bank priority order.</summary>
    public List<string> PriorityOrder
    {
        get => SoundBankList.Select(s => s.ID).ToList();
        set
        {
            Util.Sort(SoundBankList, (a, b) =>
                value.IndexOf(a.ID) - value.IndexOf(b.ID));
            GeneratePresetList();
        }
    }

    /// <summary>
    /// A getter that returns the current MIDI system.
    /// Used by the custom drum sets to resolve patches.
    /// </summary>
    public Func<Midi.System> SystemGetter = () => Midi.System.GS;

    /// <summary>Deletes a given sound bank by its ID.</summary>
    /// <param name="id">The ID of the sound bank to delete.</param>
    /// <exception cref="Exception"></exception>
    public void DeleteSoundBank(string id)
    {
        if (SoundBankList.Count == 0)
        {
            Debug.WriteLine("[WARN] 1 soundbank left. Aborting!");
            return;
        }

        if (Util.IndexOf(
                CollectionsMarshal.AsSpan(SoundBankList), 
                id, 
                (s, id) => s.ID.Equals(id)) is not {} index)
            throw SpessaException.Invalid($"No sound bank with id '{id}'");
        
        SoundBankList.RemoveAt(index);
        GeneratePresetList();
    }

    /// <summary> Resets the sound bank properties that are controllable by MIDI. </summary>
    internal void Reset()
    {
        foreach (var userDrum in UserDrumSets)
            userDrum.Reset();
    }

    /// <summary>Adds a new sound bank with a given ID, or replaces an existing one.</summary>
    /// <param name="font">The sound bank to add.</param>
    /// <param name="id">The ID of the sound bank.</param>
    /// <param name="bankOffset">The bank offset of the sound bank.</param>
    public void Add(
        SoundBank.SoundBank font, string id, int bankOffset = 0) =>
        Add(font, id, bankOffset, true);

    internal void Add(
        SoundBank.SoundBank font,
        string id,
        int bankOffset,
        bool genPresetList)
    {
        foreach (ref var entry in CollectionsMarshal.AsSpan(SoundBankList))
        {
            if (!entry.ID.Equals(id)) continue;
            // Replace
            var newEntry = new ListEntry(id, font, bankOffset);
            if (entry == newEntry) return;

            entry = newEntry;
            if (genPresetList) GeneratePresetList();
            return;
        }

        SoundBankList.Add(new ListEntry(id, font, bankOffset));
        if (genPresetList) GeneratePresetList();
    }

    /// <summary>Gets a given preset from the sound bank stack.</summary>
    /// <param name="patch">The MIDI patch to search for.</param>
    /// <param name="system">The MIDI system to select the preset for.</param>
    /// <returns>An object containing the preset and its bank offset.</returns>
    public SynthPatch? GetPreset(
        MidiPatch patch, Midi.System system) =>
        SoundBankList.Count == 0 || _selectablePresetList.Count == 0
            ? null
            : PresetSelector.Of(_selectablePresetList, patch, system);
    
    /// <summary>Clears the sound bank list and destroys all sound banks.</summary>
    public void Destroy()
    {
        foreach (var s in SoundBankList)
            s.SoundBank.Destroy();
        SoundBankList.Clear();
        _selectablePresetList.Clear();
        _presetList.Clear();
        Cache.Clear();
    }

    private void GeneratePresetList()
    {
        var presetList      = new List<SynthPatch>();
        var addedPresets    = new HashSet<MidiPatch.Full>();
        var totalPresets    = SoundBankList.Sum(
            e => e.SoundBank.Presets.Count);

        // If there are no presets, don't report as having GS user drums
        if (totalPresets > 0)
        {
            // Add custom drum sets first so they take priority
            foreach (var drumSet in UserDrumSets)
            {
                if (!addedPresets.Add(drumSet)) continue;
                presetList.Add(drumSet);
            }
        }

        foreach (var (_, bank, bankOffset) in SoundBankList)
        foreach (var p in bank.Presets)
            if (addedPresets.Add(PatchOf(p, bankOffset)))
                presetList.Add(New(p, bankOffset));
        
        Util.Sort(presetList, (a, b) =>
            MidiPatch.Compare(a.Patch.Data, b.Patch.Data));
        
        _selectablePresetList.Clear();
        _selectablePresetList.AddRange(presetList);
        
        _presetList.Clear();
        foreach (var p in presetList)
            _presetList.Add(p.Patch);
        
        _presetListChangeCallbank();
    }
    
    private static BasicPreset New(BasicPreset p, int offset)
    {
        var newPatch = PatchOf(p, offset);
        if (newPatch == p.Patch) return p;

        var result = new BasicPreset(p.Parent, p.GlobalZone)
        {
            Patch       = newPatch,
            Genre       = p.Genre,
            Morphology  = p.Morphology,
            Library     = p.Library,
        };

        result.Zones.AddRange(p.Zones);
        
        return result;
    }

    private static MidiPatch.Full PatchOf(MidiPatch.Full patch, int offset) =>
        patch with
        {
            Data = patch.Data with
            {
                BankMSB = BankSelectHacks.AddBankOffset(
                    patch.BankMSB, offset, true),
            }
        };
}