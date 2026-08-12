const bases = {
  release: "https://github.com/tiouoo/Portal/releases/latest/download",
  nightly: "https://github.com/tiouoo/Portal/releases/download/publish-nightly",
  commit: "https://github.com/tiouoo/Portal/releases/download/publish-commit"
}

const cnbBases = {
  release: "https://cnb.cool/tiouo/portal/-/releases/latest/download",
  nightly: "https://cnb.cool/tiouo/portal/-/releases/download/publish-nightly",
  commit: "https://cnb.cool/tiouo/portal/-/releases/download/publish-commit"
}

export const channelBase = (channel) => bases[channel] || bases.nightly
export const cnbBase = (channel) => cnbBases[channel] || cnbBases.nightly

export const platforms = [
  {
    id: 'windows',
    name: 'Windows',
    detail: 'Windows 10 / 11',
    icon: 'windows',
    primary: { label: 'x64 安装程序', meta: 'x64 · zip', file: 'Portal.win.x64.installer.zip' },
    links: [
      { label: 'x64 安装程序', meta: 'x64 · zip', file: 'Portal.win.x64.installer.zip' },
      { label: 'x64 便携版', meta: 'x64 · zip', file: 'Portal.win.x64.portable.zip' }
    ]
  },
  {
    id: 'macos',
    name: 'macOS',
    detail: 'Intel 与 Apple 芯片',
    icon: 'apple',
    primary: { label: 'Apple 芯片版', meta: 'arm64 · dmg', file: 'Portal.osx.mac.arm64.dmg' },
    links: [
      { label: 'Apple 芯片', meta: 'arm64 · dmg', file: 'Portal.osx.mac.arm64.dmg' },
      { label: 'Intel 芯片', meta: 'x64 · dmg', file: 'Portal.osx.mac.x64.dmg' },
      { label: 'Apple 芯片应用包', meta: 'arm64 · app.zip', file: 'Portal.osx.mac.arm64.app.zip' },
      { label: 'Intel 芯片应用包', meta: 'x64 · app.zip', file: 'Portal.osx.mac.x64.app.zip' }
    ]
  },
  {
    id: 'linux',
    name: 'Linux',
    detail: 'Linux x64',
    icon: 'linux',
    primary: { label: 'x64 AppImage', meta: 'x64 · appimage', file: 'Portal.linux.x64.AppImage' },
    links: [
      { label: 'x64 AppImage', meta: 'x64 · appimage', file: 'Portal.linux.x64.AppImage' },
      { label: 'x64 deb 包', meta: 'x64 · deb', file: 'Portal.linux.x64.deb' },
      { label: 'x64 rpm 包', meta: 'x64 · rpm', file: 'Portal.linux.x64.rpm' }
    ]
  }
]