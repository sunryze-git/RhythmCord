{
  inputs = {
    nixpkgs.url = "nixpkgs/nixos-unstable";
    flake-parts.url = "github:hercules-ci/flake-parts";
  };

  outputs = inputs@{ flake-parts, self, ... }:
    flake-parts.lib.mkFlake { inherit inputs; } {
      # The csproj pins RuntimeIdentifier=linux-x64, so only x86_64-linux is supported.
      systems = [ "x86_64-linux" ];

      perSystem = { pkgs, lib, self', ... }:
        let
          dotnet = pkgs.dotnetCorePackages.dotnet_10;
        in {
          packages.default = pkgs.buildDotnetModule {
            pname = "singularity";
            version = "0.1.0";
            # Only the files the build needs: keeps untracked clutter (bin/obj, .venv,
            # result) and gitignored secrets such as appsettings.json out of the store.
            src = lib.fileset.toSource {
              root = ./.;
              fileset = lib.fileset.unions [
                ./Singularity.sln
                ./Singularity/Singularity.csproj
                ./Singularity/.editorconfig
                ./Singularity/Program.cs
                ./Singularity/Features
                ./Singularity/Infrastructure
                ./Singularity/Observability
              ];
            };

            projectFile = "Singularity/Singularity.csproj";
            nugetDeps = ./deps.json;

            dotnet-sdk = dotnet.sdk;
            dotnet-runtime = dotnet.runtime;

            # The csproj publishes self-contained/single-file/trimmed for standalone use.
            # buildDotnetModule wants a framework-dependent build (nixpkgs dotnet-runtime
            # plus runtimeDeps/makeWrapper), and the offline restore has no runtime packs.
            dotnetFlags = [
              "-p:SelfContained=false"
              "-p:PublishSingleFile=false"
              "-p:PublishTrimmed=false"
              "-p:EnableCompressionInSingleFile=false"
              "-p:IncludeNativeLibrariesForSelfExtract=false"
            ];

            runtimeDeps = [ pkgs.libopus pkgs.libsodium ];

            makeWrapperArgs = [
              "--prefix PATH : ${lib.makeBinPath [ pkgs.ffmpeg pkgs.yt-dlp ]}"
            ];

            executables = [ "Singularity" ];
          };

          # Regenerate deps.json after changing PackageReferences:
          #   nix run .#fetch-deps -- deps.json
          apps.fetch-deps = {
            type = "app";
            program = "${self'.packages.default.passthru.fetch-deps}";
          };

          devShells.default = pkgs.mkShell {
            name = "Singularity";
            packages = [ dotnet.sdk pkgs.libopus pkgs.libsodium pkgs.ffmpeg pkgs.yt-dlp ];
            shellHook = ''
              export DOTNET_ROOT=${dotnet.sdk}/share/dotnet
              # Deliberately not appending the inherited LD_LIBRARY_PATH: a foreign one (e.g. a newer alsa-lib from the
              # host session) gets picked up ahead of ffmpeg's own libraries and breaks it with glibc version errors.
              export LD_LIBRARY_PATH="${lib.makeLibraryPath [ pkgs.libopus pkgs.libsodium ]}"
            '';
          };
        };

      flake.nixosModules.default = { config, lib, pkgs, ... }:
        let
          cfg = config.services.singularity;
        in {
          options.services.singularity = {
            enable = lib.mkEnableOption "Singularity Discord bot";
            package = lib.mkOption {
              type = lib.types.package;
              default = self.packages.${pkgs.stdenv.hostPlatform.system}.default;
            };
            environmentFile = lib.mkOption {
              type = lib.types.path;
              description = ''
                File with secrets in KEY=value form, e.g.
                Discord__Token=xxxxx
              '';
            };
          };

          config = lib.mkIf cfg.enable {
            systemd.services.singularity = {
              description = "Singularity Discord bot";
              wantedBy = [ "multi-user.target" ];
              after = [ "network-online.target" ];
              wants = [ "network-online.target" ];

              serviceConfig = {
                ExecStart = lib.getExe cfg.package;
                EnvironmentFile = cfg.environmentFile;
                Restart = "on-failure";
                RestartSec = 5;

                DynamicUser = true;
                StateDirectory = "singularity";
                WorkingDirectory = "/var/lib/singularity";

                NoNewPrivileges = true;
                ProtectSystem = "strict";
                ProtectHome = true;
                PrivateTmp = true;
              };

              # DynamicUser has no home; point caches/extraction at the state dir.
              environment = {
                HOME = "/var/lib/singularity";
                XDG_CACHE_HOME = "/var/lib/singularity/cache";
                DOTNET_BUNDLE_EXTRACT_BASE_DIR = "/var/lib/singularity/dotnet";
              };
            };
          };
        };
    };
}
