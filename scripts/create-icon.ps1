$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
$root = Split-Path $PSScriptRoot -Parent
$markup = @'
<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="256" Height="256">
 <Border Canvas.Left="8" Canvas.Top="8" Width="240" Height="240" CornerRadius="52" Background="#334f44"/>
 <Rectangle Canvas.Left="42" Canvas.Top="36" Width="172" Height="184" RadiusX="12" RadiusY="12" Fill="#f7f5ee"/>
 <Ellipse Canvas.Left="131" Canvas.Top="62" Width="50" Height="50" Fill="#d65c35"/>
 <Path Data="M42,161 L93,107 L137,151 L167,122 L214,161 L214,184 L42,184 Z" Fill="#9aaa96"/>
 <Path Data="M42,166 L79,135 L122,171 L165,146 L214,172 L214,184 L42,184 Z" Fill="#334f44"/>
 <Rectangle Canvas.Left="60" Canvas.Top="198" Width="53" Height="6" RadiusX="3" RadiusY="3" Fill="#d65c35"/>
</Canvas>
'@
$canvas=[Windows.Markup.XamlReader]::Parse($markup)
$canvas.Measure([Windows.Size]::new(256,256)); $canvas.Arrange([Windows.Rect]::new(0,0,256,256)); $canvas.UpdateLayout()
$frames=@()
foreach($size in @(16,24,32,48,64,128,256)) {
 $visual=[Windows.Media.DrawingVisual]::new(); $drawing=$visual.RenderOpen()
 $drawing.DrawRectangle([Windows.Media.VisualBrush]::new($canvas),$null,[Windows.Rect]::new(0,0,$size,$size)); $drawing.Close()
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32); $bitmap.Render($visual)
 $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new(); $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $stream=[IO.MemoryStream]::new(); $encoder.Save($stream); $frames+=,@{Size=$size;Bytes=$stream.ToArray()}; $stream.Dispose()
 if($size -eq 256){[IO.File]::WriteAllBytes((Join-Path $root 'assets/app-icon.png'),$frames[-1].Bytes)}
}
$file=[IO.File]::Create((Join-Path $root 'assets/app-icon.ico')); $writer=[IO.BinaryWriter]::new($file)
try {
 $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
 $offset=6+16*$frames.Count
 foreach($frame in $frames){$dimension=if($frame.Size -eq 256){0}else{$frame.Size};$writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frame.Bytes.Length);$writer.Write([uint32]$offset);$offset+=$frame.Bytes.Length}
 foreach($frame in $frames){$writer.Write([byte[]]$frame.Bytes)}
} finally {$writer.Dispose()}
Write-Output 'Created app-icon.ico with 16/24/32/48/64/128/256px frames and PNG preview.'
