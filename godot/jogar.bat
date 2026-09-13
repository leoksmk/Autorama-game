@echo off
rem Orbital Derby 3D: compila e abre o jogo.
rem
rem   jogar.bat                              teclado nas duas naves (ou o que ficou salvo)
rem   jogar.bat --p1=controle --p2=teclado   escolhe a entrada de cada nave
rem   jogar.bat --demo                       CPU contra CPU
rem
rem Se o Godot 4.7.2 .NET estiver instalado em outro lugar, defina a variavel
rem GODOT com o caminho completo do executavel.
setlocal
cd /d "%~dp0"

if not defined GODOT set "GODOT=%LOCALAPPDATA%\Programs\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
if not exist "%GODOT%" (
    echo Nao achei o Godot em:
    echo   %GODOT%
    echo Instale o Godot 4.7.2 .NET ou defina a variavel GODOT com o caminho do .exe.
    pause
    exit /b 1
)

dotnet build OrbitalDerby.csproj -nologo -v q
if errorlevel 1 (
    echo.
    echo A compilacao falhou - veja o erro acima.
    pause
    exit /b 1
)

start "" "%GODOT%" --path . -- %*
