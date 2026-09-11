# Wolfgate admin internet sounds

## Radio popup
wf-internet-sound-popup-title = Admin Radio
wf-internet-sound-popup-played = {$admin} played:
wf-internet-sound-popup-volume = Volume
wf-internet-sound-popup-stop = Stop

## Admin window
wf-admin-tab-internet-sound = Play Internet Sound
wf-internet-sound-admin-title = Play Internet Sound
wf-internet-sound-admin-info = Plays a YouTube, SoundCloud or other link to every connected player.
wf-internet-sound-admin-url = https://...
wf-internet-sound-admin-play = Play
wf-internet-sound-admin-stop = Stop for everyone

## Status messages
wf-internet-sound-server-name = The server
wf-internet-sound-disabled = Internet sounds are disabled on this server.
wf-internet-sound-invalid-url = That isn't an http or https link.
wf-internet-sound-fetching = Fetching {$url}...
wf-internet-sound-playing = Now playing "{$title}" for {$count} players.
wf-internet-sound-stopped = Internet sound stopped for everyone.
wf-internet-sound-error-ytdlp-missing = yt-dlp wasn't found ({$detail}). Install it or set wf.internet_sound.ytdlp_path.
wf-internet-sound-error-ffmpeg-missing = ffmpeg wasn't found ({$detail}). Install it or set wf.internet_sound.ffmpeg_path.
wf-internet-sound-error-download = yt-dlp couldn't fetch that link: {$detail}
wf-internet-sound-error-rejected = That link is a livestream or longer than the {$detail} second limit.
wf-internet-sound-error-transcode = ffmpeg couldn't convert the audio: {$detail}
wf-internet-sound-error-too-large = The converted audio is {$detail} MB, over the size limit.
wf-internet-sound-error-timeout = Fetching took too long and was cancelled.
wf-internet-sound-error-unknown = Something went wrong fetching the sound. Check the server log.

## Commands
cmd-playinternetsound-desc = Plays a YouTube, SoundCloud or other yt-dlp supported link to every connected player.
cmd-playinternetsound-help = Usage: {$command} <link>
cmd-playinternetsound-hint = <link>
cmd-playinternetsound-invalid-args = Expected exactly one link.
cmd-stopinternetsound-desc = Stops the current internet sound for everyone.
cmd-stopinternetsound-help = Usage: {$command}
