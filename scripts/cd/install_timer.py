"""Install/remove the per-user Linux timer; adopt the existing Docker stack first."""
import argparse
from pathlib import Path
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config', type=Path, default=Path(__file__).resolve().parents[2] / 'artifacts/cd/host.json')
    parser.add_argument('--remove', action='store_true')
    args = parser.parse_args()
    if sys.platform != 'linux':
        parser.error('Use Install-TeamUpdater.ps1 on Windows.')
    units = Path.home() / '.config/systemd/user'
    timer = 'autodubbing-cd.timer'
    if args.remove:
        subprocess.run(['systemctl', '--user', 'disable', '--now', timer], check=True)
        for name in (timer, 'autodubbing-cd.service'):
            (units / name).unlink(missing_ok=True)
        subprocess.run(['systemctl', '--user', 'daemon-reload'], check=True)
        return
    updater = Path(__file__).with_name('updater.py')
    subprocess.run([sys.executable, str(updater), 'verify', '--config', str(args.config.resolve())], check=True)

    def quote(value):
        return '"' + str(value).replace('\\', '\\\\').replace('"', '\\"').replace('%', '%%').replace('$', '$$') + '"'

    units.mkdir(parents=True, exist_ok=True)
    (units / 'autodubbing-cd.service').write_text(
        '[Unit]\nDescription=Update AutoDubbing API and web; preserve SQL\n'
        '[Service]\nType=oneshot\nTimeoutStartSec=1800\n'
        'ExecStart=' + ' '.join(quote(x) for x in [sys.executable, updater, 'check', '--config', args.config.resolve()]) + '\n', encoding='utf-8')
    (units / timer).write_text(
        '[Unit]\nDescription=Check the AutoDubbing release every five minutes\n'
        '[Timer]\nOnStartupSec=1min\nOnUnitInactiveSec=5min\nPersistent=true\n'
        '[Install]\nWantedBy=timers.target\n', encoding='utf-8')
    subprocess.run(['systemctl', '--user', 'daemon-reload'], check=True)
    subprocess.run(['systemctl', '--user', 'enable', '--now', timer], check=True)
    print('Installed per-user AutoDubbing CD timer; SQL/configuration unchanged.')


if __name__ == '__main__':
    main()
