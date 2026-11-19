{
  description = "Knit — git-native task tracker";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs =
    { self, nixpkgs }:
    let
      lib = nixpkgs.lib;
      forAllSystems = lib.genAttrs [
        "x86_64-linux"
        "aarch64-linux"
        "x86_64-darwin"
        "aarch64-darwin"
      ];
      version = builtins.head (
        builtins.match ".*<Version>([^<]+)</Version>.*" (builtins.readFile ./Directory.Build.props)
      );
    in
    {
      packages = forAllSystems (
        system:
        let
          pkgs = nixpkgs.legacyPackages.${system};
          inherit (pkgs) stdenv dotnetCorePackages importNpmLock;
          sdk = dotnetCorePackages.sdk_10_0;
          # nixpkgs only ships a working ILCompiler on Linux.
          aot = sdk.hasILCompiler && !stdenv.hostPlatform.isDarwin;

          spa = pkgs.buildNpmPackage {
            pname = "knit-web";
            inherit version;
            # The SPA imports the logos from ../../assets.
            src = lib.fileset.toSource {
              root = ./.;
              fileset = lib.fileset.unions [
                ./src/Knit.Web
                ./assets
              ];
            };
            sourceRoot = "source/src/Knit.Web";
            npmDeps = importNpmLock { npmRoot = ./src/Knit.Web; };
            npmConfigHook = importNpmLock.npmConfigHook;
            installPhase = "cp -r dist $out";
          };
        in
        {
          inherit spa;
          default = pkgs.buildDotnetModule {
            pname = "knit";
            inherit version;
            src = ./.;
            projectFile = "src/Knit.Cli/Knit.Cli.csproj";
            nugetDeps = ./nix-deps.json;
            dotnet-sdk = sdk;
            dotnet-runtime = if aot then null else dotnetCorePackages.aspnetcore_10_0;
            selfContainedBuild = aot;
            nativeBuildInputs = lib.optional aot stdenv.cc;
            buildInputs = lib.optional aot pkgs.zlib;
            # The SPA is built above; BuildSpa's npm ci has no network in the sandbox.
            dotnetFlags = [ "-p:SkipNpmBuild=true" ] ++ lib.optional (!aot) "-p:PublishAot=false";
            preBuild = "cp -r ${spa} src/Knit.Web/dist && chmod -R u+w src/Knit.Web/dist";
            preFixup = lib.optionalString aot "rm -f $out/lib/knit/*.dbg $out/lib/knit/*.pdb";
            executables = [ "knit" ];
            meta = {
              description = "Git-native task tracker";
              homepage = "https://github.com/MathiasReichardt/knit";
              license = lib.licenses.mit;
              mainProgram = "knit";
            };
          };
        }
      );
    };
}
