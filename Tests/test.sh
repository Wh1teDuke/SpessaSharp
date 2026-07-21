#!/bin/bash
set -e


# > Clear
case "$1" in
  --restart|--clear)
    echo "Clear cache ..."
    rm -rf generated
    ;;
esac

# > Quit
case "$1" in
  --clear)
    exit
    ;;
esac


# > Setup
if [ ! -d "generated" ]; then
  echo "Setup ..."
  mkdir generated
  cd generated

  echo "  Setup Bun"
  wget -q "https://github.com/oven-sh/bun/releases/download/canary/bun-linux-x64.zip"
  unzip -q bun-linux-x64.zip
  mv bun-linux-x64/bun .
  rm -d bun-linux-x64/
  rm bun-linux-x64.zip
  chmod +x ./bun
  BUN_INSTALL_CACHE_DIR=bcc ./bun init -y --silent > /dev/null 2>&1

  echo "  Clone SpessaSynth"
  git clone -q --depth 1 "https://github.com/spessasus/spessasynth_core.git"

  echo "  Download Sound bank (GeneralUser-GS)"
  wget -q "https://github.com/mrbumpy409/GeneralUser-GS/raw/refs/heads/main/GeneralUser-GS.sf2"
  
  echo ""
  
else
  cd generated
fi


# > Run tests
test_total=0
test_fail=0
test_success=0
failed_tests=()

test() {
  test_total=$((test_total+2))

  local ssharp="../$1"
  local ssynth="spessasynth_core/tests/$2"
  local result="$3"

  local ssharp_mid_out="${result}"
  local ssynth_mid_out="spessasynth_core/tests/midi_file/generated/${result}"
  
  local ssharp_wav_out="${ssharp_mid_out/%.mid/.wav}"
  local ssynth_wav_out="${ssynth_mid_out/%.mid/.wav}"
  
  local bank="GeneralUser-GS.sf2" # TODO: Pass as (optional) argument

  # Invalidate prev run
  rm -f "${ssharp_mid_out}" "${ssharp_wav_out}"
  rm -f "${ssynth_mid_out}" "${ssynth_wav_out}"

  printf "%-40s" "$(printf "%3d" "$((test_total/2))")) ${result} ..."

  # Execute both spessasynth and spessasharp
  #   SSynth
  BUN_INSTALL_CACHE_DIR=bcc ./bun run --silent "$ssynth" > /dev/null
  #   SSharp
  dotnet "$ssharp" > /dev/null
  
  # Render both (Use ssynth mid as reference)
  #   SSynth
  BUN_INSTALL_CACHE_DIR=bcc ./bun run --silent ./spessasynth_core/examples/midi_to_wav_node.ts "$bank" "${ssynth_mid_out}" "${ssynth_wav_out}" > /dev/null
  #   SSharp
  dotnet ../../Examples/MidiToWavNode.cs -- "$bank" "${ssynth_mid_out}" "${ssharp_wav_out}" > /dev/null
  
  for file in "${ssharp_mid_out}" "${ssharp_wav_out}" "${ssynth_mid_out}" "${ssynth_wav_out}"; do
    if [ ! -f "${file}" ]; then
      echo "!${file} Not found!"
      test_fail=$((test_fail+1))
      failed_tests+=("$result")
      return
    fi
  done

  # 1 Compare Midi
  if cmp -s "${ssharp_mid_out}" "${ssynth_mid_out}"; then
    test_success=$((test_success+1))
    echo -n "✓   "
  else
    test_fail=$((test_fail+1))
    failed_tests+=("${ssharp_mid_out}")
    echo - "✗   "
  fi
  
  # 2 Compare wav
  local threshold=0.1 # TODO: pass optional tresh
  local max_amp=$(
    sox -m -v 1 "${ssharp_wav_out}" -v -1 "${ssynth_wav_out}" -n stat 2>&1 |
    awk '/Maximum amplitude:/ {print $3}'
  )
  if (( $(echo "$max_amp < $threshold" | bc -l) )); then
    test_success=$((test_success+1))
    echo "✓"
  else
    test_fail=$((test_fail+1))
    failed_tests+=("${ssharp_wav_out} ($max_amp)")
    echo "✗"
  fi
}

# -------------------------------------
# TEST START
echo "Test start ..."
echo " Num Name                              mid wav"

# Midi
test midi/cc/SoftPedal.cs               midi_file/cc/soft_pedal.ts                soft_pedal_test.mid
test midi/cc/RPNFineTuning.cs           midi_file/cc/rpn_fine_tuning.ts           rpn_fine_tuning_test.mid
test midi/cc/RealtimeRPNTuning.cs       midi_file/cc/realtime_rpn_tuning.ts       rpn_tuning_real-time_test.mid

test midi/other/AssignMode.cs           midi_file/other/assign_mode.ts            assign_mode_test.mid
test midi/other/DrumSpam.cs             midi_file/other/drum_spam.ts              drum_spam_test.mid
test midi/other/MonoMode.cs             midi_file/other/mono_mode.ts              mono_mode_test.mid
test midi/other/OverlappingNotesTest.cs midi_file/other/overlapping_notes_test.ts overlapping_notes_test.mid

# -------------------------------------


echo ""
echo "${test_total} ($((test_total/2))) Tests. $test_success passed and $test_fail failed."
for failed in "${failed_tests[@]}"; do
  echo "  ✗  ${failed}"
done

[ "$test_fail" -eq 0 ]