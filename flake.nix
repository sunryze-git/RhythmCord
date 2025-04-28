{
    inputs = {
        nixpkgs.url = "github:nixos/nixpkgs/nixos-24.11";
        flake-parts.url = "github:hercules-ci/flake-parts";
    };

    outputs = inputs@{ flake-parts, ... }:
        flake-parts.lib.mkFlake { inherit inputs; } {
            systems = [ "x86_64-linux" "aarch64-linux" "x86_64-darwin" "aarch64-darwin" ];
            perSystem = { inputs', pkgs, ... }: {
                devShells.default = let
                    dotnet = pkgs.dotnetCorePackages.dotnet_9;
                in pkgs.mkShell {
                    name = "VLCDiscord";
                    packages = with pkgs; [
                        dotnet.sdk
                        libopus
                        libsodium
                        ffmpeg
                        yt-dlp
                    ];
                    shellHook = with pkgs; ''
                        export DOTNET_ROOT=${dotnet.sdk}/share/dotnet
                        export LD_LIBRARY_PATH="${pkgs.lib.makeLibraryPath [
                            libopus
                            libsodium
                        ]}:$LD_LIBRARY_PATH"
                    '';
                };
            };
        };
}