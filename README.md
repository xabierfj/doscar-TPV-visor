# Doscar Visor Cliente

WPF app that emulates a POS customer display (visor de cliente) for the Doscar TPV.
Doscar writes to a serial port expecting a Posiflex PD300-class display in DSP800
mode; this app listens on that port, parses the protocol and renders the
information on a screen — designed for a 7" 800x480 HDMI panel connected as a
secondary monitor on the TPV PC.

## How it works

```
Doscar TPV ──serial (com0com virtual pair)──> this app ──> customer screen
```

- Doscar sends DSP800 commands framed as `0x04 0x01 <cmd> 0x17`, followed by
  padded text payloads (width = *Caracteres por línea*, 20 by default). `P1`
  positions on line 1, `PE` on line 2, `C1X` clears the screen.
- Each complete line pair is classified and shown on one of three panels:

| Screen from Doscar         | Panel shown                       |
|----------------------------|-----------------------------------|
| `Gracias` / blank          | Welcome (idle)                    |
| product name + `PVP: ...`  | Product (name + price)            |
| `TOTAL` + amount           | Total (large amount + thank you)  |

## Windows

- **Configuración** (main window): COM port, baud rate and characters per line.
  *Guardar* saves to `config.json` next to the exe; *Reiniciar Pantalla*
  restarts the display with the new settings.
- **Visor** (customer display): opens automatically at startup. With a
  secondary monitor connected it goes borderless fullscreen on it, topmost,
  with the cursor hidden and display sleep disabled (kiosk mode). With a
  single monitor it stays a normal 800x480 window. `Esc` closes it.

## Requirements

- Windows, .NET 6 SDK to build (the published exe is self-contained).
- A [com0com](https://com0com.sourceforge.net/) virtual serial port pair:
  Doscar writes to one end, this app listens on the other.
- Serial settings (DSP800 defaults): 9600 baud, 8 data bits, no parity, 1 stop bit.

## Build and run

```
dotnet build
dotnet run
```

## Release

```
dotnet publish -c Release
```

Produces a single self-contained `DoscarVgaDriver.exe` (no .NET install needed)
in `bin/Release/net6.0-windows/win-x86/publish/`.

## Deployment

1. Copy `DoscarVgaDriver.exe` to the TPV PC.
2. Run it, set the COM port (the com0com end Doscar is not using) and save.
3. Connect the customer panel as a secondary monitor; the visor fullscreens
   onto it automatically.
4. Optional: add a shortcut to the exe in `shell:startup` to launch with Windows.

## Protocol reference

- [DSP800 command set (gist)](https://gist.github.com/andersevenrud/e2725d99b0157fc1f7f5eccf47239837)
- [Official DSP800 spec (PDF)](https://www.danapo.cz/user/related_files/dsp800.pdf)

Note: the command/payload separator is `ETB (0x17)`, matching the official spec.
(An earlier revision of this app assumed `CR (0x0D)`; observed Doscar traffic
actually uses `ETB`.)
