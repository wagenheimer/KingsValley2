"""Master original PCM audio and copy FilmCraft video without re-encoding it."""
import argparse
import json
from pathlib import Path
import subprocess


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('video',type=Path)
    parser.add_argument('mix',type=Path)
    parser.add_argument('output',type=Path)
    args=parser.parse_args()
    measurement=subprocess.run(['ffmpeg','-hide_banner','-i',str(args.mix),'-af',
        'loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json','-f','null','-'],
        capture_output=True,text=True,check=True)
    stats=json.JSONDecoder().raw_decode(measurement.stderr[measurement.stderr.rfind('{'):])[0]
    filters=('loudnorm=I=-16:TP=-1.5:LRA=11:linear=true:print_format=json'
             f':measured_I={stats["input_i"]}:measured_TP={stats["input_tp"]}'
             f':measured_LRA={stats["input_lra"]}:measured_thresh={stats["input_thresh"]}'
             f':offset={stats["target_offset"]}')
    args.output.parent.mkdir(parents=True,exist_ok=True)
    result=subprocess.run(['ffmpeg','-hide_banner','-y','-i',str(args.video),'-i',str(args.mix),
        '-map','0:v:0','-map','1:a:0','-c:v','copy','-af',filters,'-ar','48000',
        '-c:a','aac','-b:a','320k','-shortest','-movflags','+faststart',str(args.output)],
        capture_output=True,text=True,check=True)
    output=json.JSONDecoder().raw_decode(result.stderr[result.stderr.rfind('{'):])[0]
    args.output.with_suffix('.audio.json').write_text(json.dumps({'source':stats,'master':output,
        'video_reencoded':False,'audio_source':str(args.mix)},indent=2),encoding='utf-8')
    print(f'Saved {args.output}; FilmCraft video stream copied unchanged.')


if __name__=='__main__':
    main()
