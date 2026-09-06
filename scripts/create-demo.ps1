param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../.tools/demo-data'), [string]$ModelDirectory)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path (Join-Path $root 'history.json')) { throw 'Use an empty demo directory to protect existing history.' }
New-Item -ItemType Directory -Force (Join-Path $root 'History') | Out-Null
$examples = @(
    @('A little space to think', 'I want to leave a little more space in the day. A walk before the first meeting, lunch away from the screen, and time to finish one thing before starting the next.'),
    @('A note for tomorrow', 'Before we ship the update, check the keyboard shortcuts, test a fresh install, and make sure the help text sounds like something a person would say.'),
    @('Weekend plans', 'Let us take the early train on Saturday. We can find a quiet place for coffee, visit the bookshop, and spend the afternoon by the water.'),
    @('A quick reply', 'Thanks for sending this over. The new layout looks good to me. I have a few small notes on the spacing, but we should be ready to share it tomorrow.'),
    @('Recipe idea', 'Roast the tomatoes with olive oil and a pinch of salt. Add garlic near the end, then toss everything with pasta and fresh basil.'),
    @('Meeting notes', 'We agreed to keep the first release small. Focus on reliable recording, clear feedback, and a simple way to find old transcripts.'),
    @('A sentence worth keeping', 'Good tools make room for the work. They help you get an idea down while it is still fresh, then get out of the way.'),
    @('Morning list', 'Pick up the parcel, water the plants, and call about the bike repair. Leave the afternoon open for a long walk.'),
    @('Writing draft', 'The room was quiet except for the rain against the window. I made another cup of tea and opened the notebook to a clean page.'),
    @('Project sketch', 'A small app for saving the things you notice. A sentence, a photograph, a voice note. Something you can come back to without having to organize it first.'),
    @('Travel notes', 'Book a room near the station and pack light. Bring the camera, a notebook, and the comfortable shoes.'),
    @('An idea on the way home', 'What if the first screen only showed what you need right now? The rest can wait until you ask for it.')
)
$voice = New-Object -ComObject SAPI.SpVoice
$items = @()
for ($i = 0; $i -lt $examples.Count; $i++) {
    $id = 'demo-' + ($i + 1).ToString('00')
    $audio = "$id.wav"
    $stream = New-Object -ComObject SAPI.SpFileStream
    try {
        $stream.Open((Join-Path $root "History/$audio"), 3, $false)
        $voice.AudioOutputStream = $stream
        [void]$voice.Speak($examples[$i][1])
    } finally { $stream.Close(); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($stream) }
    $items += [ordered]@{ Id=$id; Timestamp=[DateTimeOffset]::Now.AddHours(-$i*3).ToString('o'); Text=$examples[$i][1]; ModelId= $(if($i % 3 -eq 0){'parakeet-v3'}else{'whisper-small'}); Duration=[Math]::Round(($examples[$i][1].Split(' ').Count / 2.5),1); Source=$examples[$i][0]; AudioFile=$audio }
}
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($voice)
$items | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $root 'history.json')
@{ModelId='parakeet-v3'; Shortcut='Alt + Space'; Retention='Audio and text'; AutoPaste=$true; RestoreClipboard=$true; TrimSilence=$true} | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $root 'settings.json')
if($ModelDirectory) { New-Item -ItemType Junction -Path (Join-Path $root 'Models') -Target ([IO.Path]::GetFullPath($ModelDirectory)) | Out-Null }
Write-Output "Created $($items.Count) fictional demo recordings in $root"
