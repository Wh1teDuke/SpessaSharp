mkdir out

for bank in banks/*; do
  for midi in midis/*; do
      midi_name=$(basename "$midi" | cut -d. -f1)
      bank_name=$(basename "$bank" | cut -d. -f1)

      echo "dotnet run -c Release SpessaSharp/Examples/MidiToWavNode.cs -- $bank $midi out/sharp__${bank_name}__${midi_name}.wav"
      dotnet run -c Release SpessaSharp/Examples/MidiToWavNode.cs -- "$bank" "$midi" "out/sharp__${bank_name}__${midi_name}.wav" > /dev/null
  done
done