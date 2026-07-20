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
  ../../.github/scripts/ssdiff/setup_ss.sh
  git clone -q --depth 1 "https://github.com/spessasus/spessasynth_core.git"
else
  cd generated
fi


# > Run tests
test_total=0
test_fail=0
test_success=0
failed_tests=()

test() {
  test_total=$((test_total+1))

  local ssharp="../$1"
  local ssynth="spessasynth_core/tests/$2"
  local result="$3"

  local ssharp_out="${result}"
  local ssynth_out="spessasynth_core/tests/midi_file/generated/${result}"

  # Invalidate prev run
  rm -f "$ssharp_out"
  rm -f "$ssynth_out"

  printf "%-40s" "$(printf "%3d" "${test_total}")) $result ..."

  # Execute both spessasynth and spessasharp
  BUN_INSTALL_CACHE_DIR=bcc ./bun run --silent "$ssynth" > /dev/null
  dotnet "$ssharp" > /dev/null

  for file in "$ssharp_out" "$ssynth_out"; do
    if [ ! -f "$file" ]; then
      echo "!$file Not found!"
      test_fail=$((test_fail+1))
      failed_tests+=("$result")
      return
    fi
  done

  # Compare
  if cmp -s "$ssharp_out" "$ssynth_out"; then
    test_success=$((test_success+1))
    echo "✓"
  else
    test_fail=$((test_fail+1))
    failed_tests+=("$result")
    echo "✗"
  fi
}

# -------------------------------------
# TEST START
echo "Test start ..."
echo ""

# Midi
test midi/cc/SoftPedal.cs midi_file/cc/soft_pedal.ts soft_pedal_test.mid
test midi/cc/RPNFineTuning.cs midi_file/cc/rpn_fine_tuning.ts rpn_fine_tuning_test.mid
test midi/cc/RealtimeRPNTuning.cs midi_file/cc/realtime_rpn_tuning.ts rpn_tuning_real-time_test.mid

# -------------------------------------


echo ""
echo "$test_total Tests. $test_success passed and $test_fail failed."
for failed in "${failed_tests[@]}"; do
  echo "   ✗ $failed"
done

[ "$test_fail" -eq 0 ]