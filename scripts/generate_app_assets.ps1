#Requires -Version 7.0
param([string]$Output = (Join-Path $PSScriptRoot '../src/Framelet/Assets'), [switch]$NormalizeMaster)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$source = Join-Path $PSScriptRoot '../src/Framelet/Assets/Brand'
New-Item -ItemType Directory -Path $Output -Force | Out-Null
Add-Type -ReferencedAssemblies System.Drawing.Common,System.Drawing.Primitives -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class IconMatte {
    // Normalize the export matte without changing the approved RGB values or object geometry.
    public static Bitmap Clean(Bitmap input) {
        var b = input.Clone(new Rectangle(0,0,input.Width,input.Height),PixelFormat.Format32bppArgb);
        var data=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
        try {
            int w=b.Width,h=b.Height,n=w*h;var bytes=new byte[data.Stride*h];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);
            var labels=new int[n];var queue=new int[n];int label=0,biggest=0,biggestCount=0;
            for(int start=0;start<n;start++) {
                if(labels[start]!=0||bytes[(start/w)*data.Stride+(start%w)*4+3]<16)continue;
                label++;int head=0,tail=1;queue[0]=start;labels[start]=label;
                while(head<tail) { int at=queue[head++],x=at%w,y=at/w;
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++) {
                        int nx=x+dx,ny=y+dy;if(nx<0||nx>=w||ny<0||ny>=h)continue;int next=ny*w+nx;
                        if(labels[next]==0&&bytes[ny*data.Stride+nx*4+3]>=16){labels[next]=label;queue[tail++]=next;}
                    }
                }
                if(tail>biggestCount){biggestCount=tail;biggest=label;}
            }
            for(int at=0;at<n;at++) { int i=(at/w)*data.Stride+(at%w)*4;
                if(biggest==0||labels[at]!=biggest){bytes[i]=bytes[i+1]=bytes[i+2]=bytes[i+3]=0;}
                else if(bytes[i+3]>=240)bytes[i+3]=255;
            }
            Marshal.Copy(bytes,0,data.Scan0,bytes.Length);
        } finally { b.UnlockBits(data); }
        b.SetResolution(96,96);return b;
    }
}
'@
function Save-Scaled($image,[int]$size,[string]$name) {
    $b=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb);$b.SetResolution(96,96)
    $g=[Drawing.Graphics]::FromImage($b);$a=[Drawing.Imaging.ImageAttributes]::new()
    try {
        $g.Clear([Drawing.Color]::Transparent);$g.CompositingMode=[Drawing.Drawing2D.CompositingMode]::SourceCopy
        $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic;$g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $a.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
        $g.DrawImage($image,[Drawing.Rectangle]::new(0,0,$size,$size),0,0,$image.Width,$image.Height,[Drawing.GraphicsUnit]::Pixel,$a)
        $b.Save((Join-Path $Output $name),[Drawing.Imaging.ImageFormat]::Png)
    } finally {$a.Dispose();$g.Dispose();$b.Dispose()}
}
function Round-Path($s,[double]$scale) {
    $p=[Drawing.Drawing2D.GraphicsPath]::new();[single]$x=$s.x*$scale;[single]$y=$s.y*$scale;[single]$w=$s.width*$scale;[single]$h=$s.height*$scale;[single]$d=2*$s.radius*$scale
    $p.AddArc($x,$y,$d,$d,180,90);$p.AddArc(($x+$w-$d),$y,$d,$d,270,90);$p.AddArc(($x+$w-$d),($y+$h-$d),$d,$d,0,90);$p.AddArc($x,($y+$h-$d),$d,$d,90,90);$p.CloseFigure();return ,$p
}
function Save-Tray($design,[int]$size) {
    $scale=$size/$design.size;$b=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb);$b.SetResolution(96,96);$g=[Drawing.Graphics]::FromImage($b)
    try {
        $g.Clear([Drawing.Color]::Transparent);$g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias;$g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        foreach($shape in @($design.back,$design.front)){$p=Round-Path $shape $scale;$brush=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($shape.fill));try{$g.FillPath($brush,$p)}finally{$brush.Dispose();$p.Dispose()}}
        $pin=$design.pin;$brush=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($pin.fill));try{$g.FillEllipse($brush,[single](($pin.cx-$pin.radius)*$scale),[single](($pin.cy-$pin.radius)*$scale),[single](2*$pin.radius*$scale),[single](2*$pin.radius*$scale))}finally{$brush.Dispose()}
        $b.Save((Join-Path $Output "Snipsera-Tray-$size.png"),[Drawing.Imaging.ImageFormat]::Png)
    } finally {$g.Dispose();$b.Dispose()}
}
function Save-Ico([string]$name,[int[]]$sizes,[bool]$trayOnly) {
    $blocks=@();foreach($size in $sizes){$prefix=if($trayOnly){'Snipsera-Tray-'}else{'Snipsera-'};$blocks+=,[IO.File]::ReadAllBytes((Join-Path $Output ($prefix+$size+'.png')))}
    $stream=[IO.File]::Create((Join-Path $Output $name));$writer=[IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Length);$offset=6+16*$sizes.Length
        for($i=0;$i -lt $sizes.Length;$i++){$s=if($sizes[$i] -eq 256){0}else{$sizes[$i]};$writer.Write([byte[]]@($s,$s,0,0));$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$blocks[$i].Length);$writer.Write([uint32]$offset);$offset+=$blocks[$i].Length}
        foreach($block in $blocks){$writer.Write([byte[]]$block)}
    } finally {$writer.Dispose();$stream.Dispose()}
}
$inputImage=[Drawing.Bitmap]::new((Join-Path $source 'master.png'));$master=$null
try {$master=[IconMatte]::Clean($inputImage)}finally{$inputImage.Dispose()}
try {
    foreach($size in @(16,20,24,30,32,36,40,44,48,50,55,60,64,66,72,80,88,96,100,110,128,132,150,176,188,200,225,256,300,375,450,512,600)){Save-Scaled $master $size "Snipsera-$size.png"}
    $designs=(Get-Content -LiteralPath (Join-Path $source 'tray.json') -Raw -Encoding utf8 | ConvertFrom-Json).sizes
    foreach($design in $designs){Save-Tray $design $design.size}
    $largeTray = $designs | Where-Object size -eq 32
    foreach($size in @(40,48,64)){Save-Tray $largeTray $size}
    foreach($base in @(44,150,50)){foreach($scale in @(200,400)){Copy-Item -LiteralPath (Join-Path $Output ('Snipsera-'+($base*$scale/100)+'.png')) -Destination (Join-Path $Output "Snipsera-$base.scale-$scale.png")}}
    foreach($size in @(16,20,24,30,32,36,40,48,60,64,72,80,96,256)){
        $file="Snipsera-$size.png"
        foreach($form in @('','_altform-unplated','_altform-lightunplated')){Copy-Item -LiteralPath (Join-Path $Output $file) -Destination (Join-Path $Output "Snipsera-44.targetsize-$size$form.png")}
    }
    Save-Ico 'Snipsera.ico' @(16,20,24,32,48,64,128,256) $false
    Save-Ico 'Snipsera-Tray.ico' @(16,20,24,32,40,48,64) $true
    if ($NormalizeMaster) {
        $temporaryMaster = Join-Path ([IO.Path]::GetTempPath()) ('snipsera-master-' + [guid]::NewGuid().ToString('N') + '.png')
        $master.Save($temporaryMaster,[Drawing.Imaging.ImageFormat]::Png)
    }
} finally {$master.Dispose()}
if ($NormalizeMaster) { Move-Item -LiteralPath $temporaryMaster -Destination (Join-Path $source 'master.png') -Force }
Write-Output "Rendered Pinned Pair app, tray and Store assets in $Output"
