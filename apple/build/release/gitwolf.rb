cask "gitwolf" do
  version "0.2.0"
  sha256 "4ddb67e1f004076978e63ae7ad9f8311e4386c34096e507fc61558ec52c89c8a"

  url "https://github.com/ipuppyyt/GitWolf/releases/download/v#{version}/GitWolf-#{version}.dmg"
  name "GitWolf"
  desc "Fast, native Git client"
  homepage "https://ipuppyyt.github.io/GitWolf/"

  livecheck do
    url "https://ipuppyyt.github.io/GitWolf/appcast.xml"
    strategy :sparkle, &:short_version
  end

  auto_updates true
  depends_on macos: :sequoia

  app "GitWolf.app"

  # GitWolf is signed with its own certificate and not notarised, so macOS would ask once on first launch.
  # Installing through Homebrew is the user's choice to trust it: clear the quarantine flag.
  postflight_steps do
    run "/usr/bin/xattr", args: ["-dr", "com.apple.quarantine", "{{appdir}}/GitWolf.app"]
  end

  zap trash: [
    "~/Library/Application Support/GitWolf",
    "~/Library/Caches/GitWolf",
    "~/Library/Preferences/org.gitwolf.GitWolf.plist",
  ]
end
