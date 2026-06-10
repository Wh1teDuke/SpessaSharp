BUN_INSTALL_CACHE_DIR=bcc

mkdir out

for bank in banks/*; do
  for midi in midis/*; do
      midi_name=$(basename "$midi" | cut -d. -f1)
      bank_name=$(basename "$bank" | cut -d. -f1)

      echo "midi_to_wav_node.ts $bank $midi out/synth__${bank_name}__${midi_name}.wav"
      ./bun midi_to_wav_node.ts "$bank" "$midi" "out/synth__${bank_name}__${midi_name}.wav" > /dev/null
  done
done