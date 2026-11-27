"""Build an editable FilmCraft impact pilot from lossless runtime PNG sequences.

FilmCraft 0.20.1 does not decode FFV1. PNG sequences avoid a lossy transcode.
Only generated project files under the chosen output directory are modified.
"""
import argparse
import json
from pathlib import Path
import subprocess
import textwrap
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
TICKS = 254016000000


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cli', type=Path, default=Path('C:/Program Files/ArtCraft/FilmCraft/filmcraft-cli.exe'))
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/trailer/filmcraft')
    parser.add_argument('--export', action='store_true')
    parser.add_argument('--edit', type=Path, help='Approved 220-second edit, or 45-second portrait edit.')
    parser.add_argument('--vertical', action='store_true')
    parser.add_argument('--clean', action='store_true')
    parser.add_argument('--pilot', type=float, help='Limit an approved edit to its first N seconds.')
    parser.add_argument('--music', type=Path)
    parser.add_argument('--effects', type=Path)
    parser.add_argument('--glow', action='store_true', help='Add a brief authentic FilmCraft glow to title graphics only.')
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    project = output / ('trailer.fcproj' if args.edit else 'impact-pilot.fcproj')
    if project.exists():
        raise RuntimeError(f'Preserve the existing edit: choose another --output directory: {project}')
    sources = ROOT / 'artifacts/trailer/frames'
    log = []

    def command(identifier, params):
        # Structured JSON is passed directly to the process, without shell escaping.
        invocation = [str(args.cli), '--project', str(project), '--save', 'exec', identifier, json.dumps(params)]
        result = subprocess.run(invocation, capture_output=True, text=True)
        log.append({'id': identifier, 'params': params, 'stdout': result.stdout, 'stderr': result.stderr})
        (output / 'commands.json').write_text(json.dumps(log, indent=2), encoding='utf-8')
        if result.returncode:
            raise RuntimeError(result.stderr or result.stdout)
        return json.loads(result.stdout)

    width, height = (1080,1920) if args.vertical else (1920,1080)
    subprocess.run([str(args.cli), '--save-as', str(project), 'exec', 'file.newSequence',
                    json.dumps({'name': "King's Valley II - Trailer",
                                'height':height, 'fps':60, 'video':6, 'audio':2, 'width':width})], check=True)
    takes = [('door', 'RETURN TO THE\nPYRAMIDS', 'THE ADVENTURE RETURNS'),
             ('boomerang', 'MAKE EVERY\nTHROW COUNT', 'TOOLS. WEAPONS. TACTICS.'),
             ('dig', 'BREAK\nTHROUGH', 'FIND YOUR OWN WAY')]
    shots = [dict(clip=name, title=title, section=kicker, duration=5, start=0, subtitle='') for name,title,kicker in takes]
    if args.edit:
        edit = json.loads(args.edit.read_text(encoding='utf-8-sig'))
        shots = edit if isinstance(edit,list) else edit['shots']
        expected = 45 if args.vertical else 220
        if abs(sum(float(s['duration']) for s in shots)-expected) > 1/60:
            raise ValueError(f'Edit must total {expected} seconds.')
    total = sum(float(s['duration']) for s in shots)
    if args.pilot is not None:
        if not 0 < args.pilot <= total:
            raise ValueError('Pilot duration must be within the approved edit.')
        total = args.pilot
    imported = {}
    at = 0.0
    for index, shot in enumerate(shots):
        if at >= total - 1/120:
            break
        name, title, kicker = shot['clip'], shot.get('title',''), shot.get('section','')
        start = float(shot.get('start',0))
        duration = min(float(shot['duration']),total-at)
        if start < 0 or duration <= 0:
            raise ValueError(f'Invalid shot range: {name}')
        sequence = output / 'sources' / name
        sequence.mkdir(parents=True, exist_ok=True)
        frames = sorted((sources / name).glob('frame*.png'))
        if len(frames) < round((start+float(shot['duration']))*60):
            raise RuntimeError(f'Insufficient authentic source frames: {name}; need {(start+float(shot["duration"]))*60}, have {len(frames)}')
        with Image.open(frames[0]) as image:
            source_size = image.size
        editor = source_size[0] > 512
        if name not in imported:
            for number, frame in enumerate(frames):
                with Image.open(frame) as image:
                    if not editor:
                        if image.size not in [(256,212),(256,192)]:
                            raise ValueError(f'Unexpected native source size: {image.size}')
                        image = image.resize((1024,image.height*4), Image.Resampling.NEAREST)
                    image.save(sequence / f'frame_{number:04d}.png')
            item = command('file.importImageSequence', {'path':str(sequence / 'frame_0000.png')})['items'][0]
            imported[name] = item
        item = imported[name]
        # The installed image sequence decoder defaults to 30000/1001, and its
        # interpretation override does not affect decoding. Use the documented
        # speed command to map every original frame back to the 60 fps timeline.
        info = json.loads(subprocess.check_output([str(args.cli), 'probe',
                     str(sequence/'frame_0000.png'), '--image-sequence'], text=True))
        rate = info['video']['frame_rate']
        speed = 60 * rate['den'] / rate['num']
        clip = command('timeline.place', {'item':item,'track':'V1','seconds':at,
                                          'sourceIn':{'seconds':start*speed},
                                          'duration':{'seconds':duration*speed},'audio':False})['clips'][0]
        command('clip.speedDuration', {'clips':[clip], 'speed':100*speed,
                'reverse':False, 'ripple':False, 'interpolation':'frameSampling'})
        command('effects.setParam', {'clip':clip,'effect':'motion','param':'position','value':[540,844] if args.vertical else ([960,450] if editor else [592,540])})
        if editor and args.vertical:
            command('effects.setParam', {'clip':clip,'effect':'motion','param':'scale','value':min(1024/source_size[0],700/source_size[1])*100})
        if args.clean:
            at += duration
            continue
        tx, ty = (72,174) if args.vertical else ((85,956) if editor else (1130,330))
        title_size = 65 if args.vertical else (47 if editor else 78)
        title = '\n'.join(textwrap.wrap(title.upper(),24 if args.vertical else (60 if editor else 17)))
        if len(title.splitlines()) > (3 if not editor else 2):
            raise ValueError(f'Title exceeds its safe area: {name}')
        graphic = command('graphics.newText', {'text':title,'position':[1130,330],
                          'box':[930 if args.vertical else (1750 if editor else 740),260 if args.vertical else 440],'size':title_size,'font':'Segoe UI','fontStyle':'Bold',
                          'seconds':duration,'track':1,'time':round(at*TICKS)})['clip']
        command('graphics.set', {'clip':graphic,'layer':0,'props':{'fill_color':'#FFE09A','shadow':True,
                                 'tracking':10,'size':title_size,'font_style':'Bold'}})
        if args.glow:
            command('effects.apply', {'clips':[graphic],'effect':'wonder_glow'})
            command('effects.setParam', {'clip':graphic,'effect':'wonder_glow','param':'radius','value':8})
            for moment, intensity in [(0,0),(.12,25),(.45,0)]:
                command('effects.setParam', {'clip':graphic,'effect':'wonder_glow','param':'intensity',
                        'value':intensity,'time':round((at+moment)*TICKS),'keyframe':True})
        for time, scale, position, opacity in [(0,120,[tx,ty+45],0),(.10,108,[tx,ty-10],100),
                                               (.28,100,[tx,ty],100),(duration-.35,100,[tx,ty],100),
                                               (duration-.05,104,[tx+40,ty],0)]:
            command('graphics.set', {'clip':graphic,'layer':0,'time':round((at+time)*TICKS),
                    'keyframe':True,'props':{'scale':scale,'position':position,'opacity':opacity}})
        kicker = '\n'.join(textwrap.wrap(kicker.upper(),50 if args.vertical else (90 if editor else 34)))
        detail = command('graphics.newText', {'text':kicker,'position':[72,110] if args.vertical else ([85,920] if editor else [1135,230]),
                         'size':22 if editor else 26,'font':'Segoe UI','fontStyle':'Bold','seconds':duration,
                         'track':2,'time':round(at*TICKS)})['clip']
        command('graphics.set', {'clip':detail,'layer':0,'props':{'fill_color':'#FFFFFF','tracking':20}})
        subtitle = '\n'.join(textwrap.wrap(shot.get('subtitle',''),60 if args.vertical else (110 if editor else 40)))
        detail = command('graphics.newText', {'text':subtitle,'position':[72,1340] if args.vertical else ([85,1020] if editor else [1135,700]),
                         'box':[930 if args.vertical else (1750 if editor else 720),300],
                         'size':28,'font':'Segoe UI','seconds':duration,'track':4,'time':round(at*TICKS)})['clip']
        command('graphics.set',{'clip':detail,'layer':0,'props':{'fill_color':'#F3EFE6'}})
        brand = command('graphics.newText', {'text':"KING'S VALLEY II" if not editor else '', 'position':[72,1750] if args.vertical else [1135,875],
                        'size':31,'font':'Segoe UI','fontStyle':'Bold','seconds':duration,
                        'track':3,'time':round(at*TICKS)})['clip']
        command('graphics.set', {'clip':brand,'layer':0,'props':{'fill_color':'#D4B866'}})
        at += duration
    for number, path in enumerate([args.music or ROOT/'artifacts/trailer/music.wav',args.effects],1):
        if path and path.exists():
            item = command('file.import', {'paths':[str(path.resolve())]})['items'][0]
            audio_clip = command('timeline.place', {'item':item,'track':f'A{number}','seconds':0,'duration':{'seconds':total},'video':False})['clips'][0]
            if number == 1:
                command('effects.setParam', {'clip':audio_clip,'effect':'volume','param':'level','value':-2.4987747})
    subprocess.run([str(args.cli),'--project',str(project),'render','--seconds','1',
                    '--out',str(output / 'impact-preview.png')],check=True)
    if args.export:
        export_name = ('trailer-clean-lossy-master.mp4' if args.clean else 'trailer.mp4') if args.edit else 'impact-pilot.mp4'
        command('file.exportMedia', {'path':str(output/export_name),'format':'h264',
                'width':width,'height':height,'fps':60,'bitrateKbps':40000,'bitrateMode':'vbr1Pass',
                'range':'custom','startSeconds':0,'endSeconds':total,
                'hardwareEncoding':'auto','audio':True,'quality':100,'wait':True})
    print(f'Saved editable FilmCraft project: {project}')


if __name__ == '__main__':
    main()
