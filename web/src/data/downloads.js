const releaseBase = 'https://github.com/tiouoo/Portal/releases/download/publish-nightly'

export const platforms = [
  {
    id: 'windows',
    name: 'Windows',
    detail: 'Windows 10 / 11',
    icon: 'windows',
    primary: 'x64 安装程序',
    primaryUrl: `${releaseBase}/Portal.win.x64.installer.exe`,
    links: [
      { label: 'x64 安装程序', meta: 'x64 · exe', url: `${releaseBase}/Portal.win.x64.installer.exe` },
      { label: 'x64 便携版', meta: 'x64 · zip', url: `${releaseBase}/Portal.win.x64.portable.zip` }
    ]
  },
  {
    id: 'macos',
    name: 'macOS',
    detail: 'Intel 与 Apple 芯片',
    icon: 'apple',
    primary: 'Apple 芯片版',
    primaryUrl: `${releaseBase}/Portal.osx.mac.arm64.dmg`,
    links: [
      { label: 'Apple 芯片', meta: 'arm64 · dmg', url: `${releaseBase}/Portal.osx.mac.arm64.dmg` },
      { label: 'Intel 芯片', meta: 'x64 · dmg', url: `${releaseBase}/Portal.osx.mac.x64.dmg` },
      { label: 'Apple 芯片应用包', meta: 'arm64 · app.zip', url: `${releaseBase}/Portal.osx.mac.arm64.app.zip` },
      { label: 'Intel 芯片应用包', meta: 'x64 · app.zip', url: `${releaseBase}/Portal.osx.mac.x64.app.zip` }
    ]
  },
  {
    id: 'linux',
    name: 'Linux',
    detail: 'AppImage 免安装运行',
    icon: 'linux',
    primary: 'x64 AppImage',
    primaryUrl: `${releaseBase}/Portal.linux.x64.AppImage`,
    links: [
      { label: '通用桌面版', meta: 'x64 · appimage', url: `${releaseBase}/Portal.linux.x64.AppImage` },
      { label: 'ARM64 版', meta: 'arm64 · appimage', url: `${releaseBase}/Portal.linux.arm64.AppImage` },
      { label: 'ARM 版', meta: '32-bit arm · appimage', url: `${releaseBase}/Portal.linux.arm.AppImage` }
    ]
  }
]
