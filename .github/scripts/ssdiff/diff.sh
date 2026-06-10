# https://github.com/FluidSynth/fluidsynth/blob/master/test/run-manual-regression.sh

extract_stat() {
  local pattern="$1"
  awk -F: -v pat="$pattern" '$1 ~ pat { gsub(/^[ \t]+/, "", $2); print $2; exit }'
}

failures=0
compared=0

SNR_MIN=60
RMS_MAX=0.0001
ABS_MAX=0.01

for bank in banks/*; do
  for midi in midis/*; do
      midi_name=$(basename "$midi" | cut -d. -f1)
      bank_name=$(basename "$bank" | cut -d. -f1)

      synth="out/synth__${bank_name}__${midi_name}.wav";
      sharp="out/sharp__${bank_name}__${midi_name}.wav";

      signal_stats=$(sox "$synth" -n stat 2>&1)
      rms_signal=$(printf '%s\n' "$signal_stats" | extract_stat "RMS[[:space:]]+amplitude")

      diff_stats=$(sox -m -v 1 "$sharp" -v -1 "$synth" -n stat 2>&1)
      rms_diff=$(printf '%s\n' "$diff_stats" | extract_stat "RMS[[:space:]]+amplitude")
      abs_diff=$(printf '%s\n' "$diff_stats" | extract_stat "Maximum amplitude")
      snr_value=$(awk -v signal="$rms_signal" -v noise="$rms_diff" 'BEGIN { if (noise == 0) { print 1e9; } else { print 20*log(signal/noise)/log(10); } }')
      snr_display=$(awk -v value="$snr_value" 'BEGIN { if (value > 1e8) { print "inf"; } else { printf "%.2f", value; } }')

      echo "${bank_name}__${midi_name}.wav" snr:"$snr_display" rms:"$rms_diff" abs:"$abs_diff"

      if ! awk -v value="$snr_value" -v min="$SNR_MIN" 'BEGIN { exit !(value >= min) }'; then
        echo "  SNR below threshold (${SNR_MIN})" >&2
        failures=$((failures + 1))
      fi
      if ! awk -v value="$rms_diff" -v max="$RMS_MAX" 'BEGIN { exit !(value <= max) }'; then
        echo "  RMS above threshold (${RMS_MAX})" >&2
        failures=$((failures + 1))
      fi
      if ! awk -v value="$abs_diff" -v max="$ABS_MAX" 'BEGIN { exit !(value <= max) }'; then
        echo "  ABS above threshold (${ABS_MAX})" >&2
        failures=$((failures + 1))
      fi
  done
done
