"""Verify exported trailer streams, decode integrity and representative frames."""
import argparse
import json
from pathlib import Path
import subprocess
from PIL import Image, ImageDraw


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('video', type=Path)
    parser.add_argument('--edit', type=Path, required=True)
    parser.add_argument('--vertical', action='store_true')
    args = parser.parse_args()
    info = json.loads(subprocess.check_output(['ffprobe','-v','error','-show_streams',
                      '-show_format','-of','json',str(args.video)],text=True))
    video = next(s for s in info['streams'] if s['codec_type']=='video')
    audio = next(s for s in info['streams'] if s['codec_type']=='audio')
    expected = 45 if args.vertical else 220
    dimensions = (1080,1920) if args.vertical else (1920,1080)
    assert (video['width'],video['height']) == dimensions, video
    assert video['r_frame_rate']=='60/1', video
    assert abs(float(info['format']['duration'])-expected)<.1, info['format']
    assert audio['codec_name']=='aac' and int(audio['sample_rate'])==48000, audio
    decode = subprocess.run(['ffmpeg','-v','error','-i',str(args.video),'-f','null','-'],
                            capture_output=True,text=True,check=True)
    if decode.stderr.strip():
        raise RuntimeError(decode.stderr)
    edit = json.loads(args.edit.read_text(encoding='utf-8'))
    folder = args.video.parent / 'review'
    folder.mkdir(exist_ok=True)
    width,height=(180,320) if args.vertical else (320,180)
    shots=edit['shots']; columns=5 if args.vertical else 4
    sheet=Image.new('RGB',(columns*width,((len(shots)+columns-1)//columns)*(height+28)),(11,16,24))
    draw=ImageDraw.Draw(sheet);time=0
    for index,shot in enumerate(shots):
        midpoint=time+shot['duration']/2
        path=folder/f'{index:02}.png'
        subprocess.run(['ffmpeg','-v','error','-y','-ss',str(midpoint),'-i',str(args.video),
                        '-frames:v','1',str(path)],check=True)
        with Image.open(path) as frame:
            frame.thumbnail((width,height))
            x=index%columns*width;y=index//columns*(height+28)
            sheet.paste(frame,(x,y+28))
            draw.text((x+4,y+5),f'{index+1:02} {midpoint:.1f}s {shot["clip"]}',fill='white')
        time+=shot['duration']
    sheet.save(folder/'contact-sheet.jpg',quality=92)
    report={'video':str(args.video),'duration':float(info['format']['duration']),
            'dimensions':dimensions,'fps':60,'video_codec':video['codec_name'],
            'audio_codec':audio['codec_name'],'frames':video.get('nb_frames'),
            'bytes':int(info['format']['size']),'full_decode_errors':0,
            'sampled_shots':len(shots),'contact_sheet':str(folder/'contact-sheet.jpg'),
            'review_limit':'Technical checks and sampled frames do not substitute for human audio and editorial approval.'}
    (folder/'verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2))


if __name__=='__main__':
    main()
