#!/usr/bin/env python3
"""Render an evidence-based trailer from real, pre-recorded footage."""
import argparse
import json
import math
import subprocess
import textwrap
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def run(args):
    print(subprocess.list2cmdline([str(a) for a in args]), flush=True)
    subprocess.run([str(a) for a in args], check=True)


def probe(path):
    return json.loads(subprocess.check_output([
        'ffprobe', '-v', 'error', '-show_streams', '-show_format', '-of', 'json', str(path)
    ], text=True))


def ass_text(value, width):
    value = str(value or '').replace('\\', '').replace('{', '').replace('}', '')
    return r'\N'.join(textwrap.wrap(value, width=width))


def subtitles(path, shot, duration, editor, vertical=False):
    # ASS uses exact output pixel coordinates; timing is local to this shot.
    if vertical:
        for field, width, limit in [('title', 24, 3), ('subtitle', 58, 5), ('section', 42, 1)]:
            if len(textwrap.wrap(str(shot.get(field, '')), width)) > limit:
                raise ValueError(f'Portrait {field} exceeds its safe text area; shorten it to {limit} lines of {width} characters.')
    header = '''[Script Info]
ScriptType: v4.00+
PlayResX: 1920
PlayResY: 1080
WrapStyle: 2
ScaledBorderAndShadow: yes
[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Title,Segoe UI,54,&H00EAF2F7,&H00EAF2F7,&H00000000,&H00000000,-1,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1
Style: Body,Segoe UI,27,&H00CCD3DA,&H00CCD3DA,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1
Style: Kicker,Segoe UI,20,&H0066B8D4,&H0066B8D4,&H00000000,&H00000000,-1,0,0,0,100,100,3,0,1,0,0,7,0,0,0,1
[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
'''
    if vertical:
        header = header.replace('PlayResX: 1920', 'PlayResX: 1080').replace('PlayResY: 1080', 'PlayResY: 1920')
    x, y = (72, 130) if vertical else ((105, 885) if editor else (1190, 370))
    entries = [
        ('Kicker', x, y, str(shot.get('section', '')).upper(), 42),
        ('Title', x, y + 44, shot.get('title', ''), 24 if vertical else (55 if editor else 21)),
        ('Body', x, 1360 if vertical else y + (112 if editor else 235), shot.get('subtitle', ''), 58 if vertical else (100 if editor else 36)),
    ]
    if vertical or not editor:
        entries += [('Kicker', 72 if vertical else 1190, 1750 if vertical else 884, "KING'S VALLEY II", 40),
                    ('Body', 72 if vertical else 1190, 1790 if vertical else 921, 'THE SEAL OF EL GIZA', 40)]
    end = f'{int(duration)//3600}:{int(duration)//60%60:02}:{duration%60:05.2f}'
    for style, px, py, value, width in entries:
        if value:
            header += (f'Dialogue: 0,0:00:00.00,{end},{style},,0,0,0,,'
                       + '{' + f'\\move({px+25},{py},{px},{py},0,350)\\fad(350,300)' + '}'
                       + ass_text(value, width) + '\n')
    path.write_text(header, encoding='utf-8-sig')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--edit', type=Path, default=ROOT / 'artifacts/trailer/edit.json')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--music', type=Path)
    parser.add_argument('--effects', type=Path, help='Full-length effects WAV synchronized to the edit.')
    parser.add_argument('--music-gain', type=float, default=.75)
    parser.add_argument('--effects-gain', type=float, default=1.0)
    parser.add_argument('--clean', action='store_true', help='Omit all editorial text and rules.')
    parser.add_argument('--vertical', action='store_true', help='Render a 45-second 1080x1920 portrait edit.')
    parser.add_argument('--pilot', type=float, help='Render only this many seconds from the beginning.')
    parser.add_argument('--thumbnail', action='store_true', help='Extract a PNG from the finished video.')
    parser.add_argument('--quality-check', action='store_true', help='Measure representative encoded frame PSNR against the filtered source.')
    args = parser.parse_args()
    edit = args.edit.resolve()
    data = json.loads(edit.read_text(encoding='utf-8-sig'))
    shots = data if isinstance(data, list) else data['shots']
    total = sum(float(s['duration']) for s in shots)
    expected = 45 if args.vertical else 220
    if abs(total - expected) > 1 / 60:
        raise ValueError(f'The approved edit must total {expected} seconds; found {total:.3f}.')
    if args.pilot is not None and not 0 < args.pilot <= total:
        raise ValueError('--pilot must be greater than zero and no longer than the edit.')
    target = args.pilot or total
    output = (args.output or edit.parent / ('trailer-clean-lossy-master.mp4' if args.clean else 'trailer.mp4')).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    work = output.parent / (output.stem + '-work')
    work.mkdir(exist_ok=True)
    rendered, elapsed = [], 0.0
    for index, shot in enumerate(shots):
        if elapsed >= target - .0001:
            break
        clip = Path(str(shot['clip']))
        if not clip.suffix:
            clip = Path('clips') / (str(clip) + '.mkv')
        clip = clip if clip.is_absolute() else edit.parent / clip
        info = probe(clip)
        video = next(s for s in info['streams'] if s['codec_type'] == 'video')
        start = float(shot.get('start', 0))
        requested = float(shot['duration'])
        duration = min(requested, target - elapsed)
        available = float(video.get('duration') or info['format']['duration'])
        if start < 0 or requested <= 0 or start + requested > available + 0.02:
            raise ValueError(f'Shot {index}: {clip} cannot supply {requested}s from {start}s (available {available}s). No looping or frozen frames is permitted.')
        w, h = int(video['width']), int(video['height'])
        editor = w > 512 or h > 424
        if args.vertical and editor:
            scale = min(1024 / w, 700 / h, 1.0)
            sw, sh = math.floor(w * scale / 2) * 2, math.floor(h * scale / 2) * 2
            filters = f'scale={sw}:{sh}:flags=lanczos,pad=1080:1920:(ow-iw)/2:650:color=0x0B1018'
        elif args.vertical:
            if (w, h) not in [(256, 212), (256, 192)]:
                raise ValueError(f'Unexpected gameplay dimensions {w}x{h}; provide native footage.')
            filters = f'scale=1024:{h*4}:flags=neighbor,pad=1080:1920:28:{420+(848-h*4)//2}:color=0x0B1018'
        elif editor:
            scale = min(1920 / w, 1080 / h, 1.0)
            sw, sh = math.floor(w * scale / 2) * 2, math.floor(h * scale / 2) * 2
            filters = f'scale={sw}:{sh}:flags=lanczos,pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=0x0B1018'
        else:
            if (w, h) not in [(256, 212), (256, 192)]:
                raise ValueError(f'Unexpected gameplay dimensions {w}x{h}; provide native 256x212 or 256x192 footage.')
            filters = f'scale=1024:{h*4}:flags=neighbor,pad=1920:1080:80:(oh-ih)/2:color=0x0B1018'
        filters += ',setsar=1,fps=60,setpts=PTS-STARTPTS,setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709'
        if not args.clean:
            ass = work / f'{index:03}.ass'
            subtitles(ass, shot, duration, editor, args.vertical)
            # Filter paths are relative to cwd to avoid Windows drive-colon escaping.
            escaped = ass.as_posix().replace(':', r'\:').replace("'", r"\'")
            if editor and not args.vertical:
                filters += ',drawbox=x=0:y=858:w=1920:h=222:color=0x0B1018@0.96:t=fill'
            filters += f',drawbox=x={72 if args.vertical else (105 if editor else 1190)}:y={108 if args.vertical else (868 if editor else 348)}:w=70:h=3:color=0xD4B866:t=fill'
            filters += f",ass=filename='{escaped}'"
        destination = work / f'{index:03}.mp4'
        run(['ffmpeg', '-hide_banner', '-y', '-ss', start, '-i', clip, '-t', duration,
             '-vf', filters, '-an', '-c:v', 'libx264', '-preset', 'medium', '-crf', '12' if args.clean else '14',
             '-pix_fmt', 'yuv420p', '-color_primaries', 'bt709', '-color_trc', 'bt709', '-colorspace', 'bt709',
             '-video_track_timescale', '60000', destination])
        if args.quality_check:
            ref, enc = work / f'{index:03}-reference.png', work / f'{index:03}-encoded.png'
            frame = min(60, max(0, round(duration * 60) // 2))
            run(['ffmpeg', '-v', 'error', '-y', '-ss', start, '-i', clip, '-vf', filters + f',select=eq(n\\,{frame})', '-frames:v', '1', ref])
            run(['ffmpeg', '-v', 'error', '-y', '-i', destination, '-vf', f'select=eq(n\\,{frame})', '-frames:v', '1', enc])
            run(['ffmpeg', '-hide_banner', '-i', ref, '-i', enc, '-lavfi', 'psnr', '-f', 'null', '-'])
        rendered.append(destination)
        elapsed += duration
    listing = work / 'concat.txt'
    listing.write_text(''.join("file '" + p.as_posix().replace("'", "'\\''") + "'\n" for p in rendered), encoding='utf-8')
    music = args.music or edit.parent / 'music.wav'
    command = ['ffmpeg', '-hide_banner', '-y', '-f', 'concat', '-safe', '0', '-i', listing]
    audio_inputs = [(music, args.music_gain, True)] if music.exists() else []
    if args.effects:
        audio_inputs.append((args.effects, args.effects_gain, False))
    audio_filters, labels = [], []
    for number, (audio_path, gain, is_music) in enumerate(audio_inputs, 1):
        audio_info = probe(audio_path)
        if float(audio_info['format']['duration']) < target - .02:
            raise ValueError(f'Audio {audio_path} is shorter than {target}s; provide a complete synchronized track. It will not be looped.')
        command += ['-i', audio_path]
        fades = f',afade=t=in:d=1,afade=t=out:st={max(0,target-3)}:d=3' if is_music else ''
        audio_filters.append(f'[{number}:a:0]asetpts=PTS-STARTPTS,volume={gain}{fades}[a{number}]')
        labels.append(f'[a{number}]')
    if labels:
        audio_filters.append(''.join(labels) + f'amix=inputs={len(labels)}:duration=longest:normalize=0,alimiter=limit=0.95:level=0:latency=1[aout]')
        command += ['-filter_complex', ';'.join(audio_filters), '-map', '0:v:0', '-map', '[aout]', '-c:a', 'aac', '-b:a', '320k']
    else:
        print('No soundtrack supplied: output intentionally contains no audio stream.')
        command += ['-an']
    command += ['-c:v', 'copy', '-t', target, '-movflags', '+faststart', output]
    run(command)
    final = probe(output)
    if abs(float(final['format']['duration']) - target) > .1:
        raise RuntimeError('Final duration does not match the edit.')
    if args.thumbnail:
        run(['ffmpeg', '-v', 'error', '-y', '-ss', min(8, target / 2), '-i', output, '-frames:v', '1', output.with_suffix('.png')])
    print(f'Rendered {output} ({target:.3f}s). H.264 is high-quality lossy, not lossless.')


if __name__ == '__main__':
    main()
