"""Compare actual current brush pixels with the pinned pre-cache implementation.

Requires git, .NET 10 SDK and SkiaSharp 2.88.9 (same dependency as the probe).
Generated baseline source/build files remain in a new diagnostic directory.
"""
import argparse
import hashlib
from pathlib import Path
import shutil
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("dotnet", help="Path to the .NET SDK executable")
parser.add_argument("output", type=Path, help="New temporary directory")
args = parser.parse_args()
root = Path(__file__).resolve().parents[3]
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=False)
baseline = subprocess.check_output(
    ["git", "show", "8cf1253:experiments/windows/avalonia/SoftBrushStroke.cs"], cwd=root
)
assert hashlib.sha256(baseline).hexdigest() == "13bd43ecff57f7b06bedffae6781da1e9d8dc027d9bdb13ef108cad75d948e8c"
source = baseline.decode().replace("internal readonly record struct BrushPoint(double X, double Y);", "")
source = source.replace("internal sealed record SoftBrushSettings(int Diameter, double Opacity, double[] Color);", "")
source = source.replace("SoftBrushStroke", "BaselineSoftBrushStroke").replace("BrushSession", "BaselineBrushSession")
(output / "BaselineSoftBrushStroke.cs").write_text(source)
for name in ("SoftBrushStroke.cs", "TiledRaster.cs"):
    shutil.copy2(root / "experiments/windows/avalonia" / name, output / name)
shutil.copy2(Path(__file__).with_name("CoverageRegression.cs"), output / "CoverageRegression.cs")
(output / "Program.cs").write_text("CoverageRegression.Run();\n")
(output / "Regression.csproj").write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseAppHost>false</UseAppHost>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="SkiaSharp" Version="2.88.9" /></ItemGroup>
</Project>
''')
subprocess.run([args.dotnet, "build", "-c", "Release"], cwd=output, check=True)
subprocess.run([args.dotnet, str(output / "bin/Release/net10.0/Regression.dll")], cwd=root, check=True)
