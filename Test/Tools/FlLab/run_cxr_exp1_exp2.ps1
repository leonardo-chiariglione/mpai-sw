# N-33: experiment 1 (C#, one End User per patient, 20 rounds, two tasks), then experiment 2 (PyTorch, the last DenseNet block fine-tuned, 5 rounds, two tasks).
$dir = 'D:\AI\Test\Results\2026-11-17'
New-Item -ItemType Directory -Force $dir | Out-Null
$log1 = "$dir\cxr-per-patient-exp1.log"; $log2 = "$dir\cxr-per-patient-exp2.log"
"started $(Get-Date -Format 'HH:mm:ss')" | Out-File $log1 -Encoding utf8
Set-Location D:\AI\Test\Mpai.Aif.Tests
dotnet test -c Release --no-build --filter "FullyQualifiedName~AihCxrPerPatientTests.ExperimentOneOnAllTheImages" --logger "console;verbosity=detailed" 2>&1 | Out-File $log1 -Append -Encoding utf8
"ended $(Get-Date -Format 'HH:mm:ss')" | Out-File $log1 -Append -Encoding utf8
Set-Location D:\AI
"started $(Get-Date -Format 'HH:mm:ss')" | Out-File $log2 -Encoding utf8
& D:\AI\Support\Python\pyannote-venv\Scripts\python.exe -I Test\Tools\FlLab\cxr_exp2.py Test\Data\ChestXray\features Test\Data\Fl\txv Test\Reports\aih-cxr-per-patient-exp2.json --workers 6 2>&1 | Out-File $log2 -Append -Encoding utf8
"ended $(Get-Date -Format 'HH:mm:ss')" | Out-File $log2 -Append -Encoding utf8
