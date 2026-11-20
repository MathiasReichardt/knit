{
  lib,
  stdenv,
  buildDotnetModule,
  buildNpmPackage,
  importNpmLock,
  dotnetCorePackages,
  zlib,
  git,
  rev ? "unknown",
}:

let
  # MinVer owns the binary's version (v* tags); the sandbox has no .git.
  version = "0-unstable-${rev}";
  sdk = dotnetCorePackages.sdk_10_0;
  # nixpkgs only ships a working ILCompiler on Linux.
  aot = sdk.hasILCompiler && !stdenv.hostPlatform.isDarwin;

  spa = buildNpmPackage {
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
buildDotnetModule {
  pname = "knit";
  inherit version;
  src = ./.;
  projectFile = "src/Knit.Cli/Knit.Cli.csproj";
  nugetDeps = ./nix-deps.json;
  dotnet-sdk = sdk;
  dotnet-runtime = if aot then null else dotnetCorePackages.aspnetcore_10_0;
  selfContainedBuild = aot;
  # MinVer fails without git on PATH, even when it then finds no repo.
  nativeBuildInputs = [ git ] ++ lib.optional aot stdenv.cc;
  buildInputs = lib.optional aot zlib;
  # The SPA is built above; BuildSpa's npm ci has no network in the sandbox.
  dotnetFlags = [ "-p:SkipNpmBuild=true" ] ++ lib.optional (!aot) "-p:PublishAot=false";
  preBuild = "cp -r ${spa} src/Knit.Web/dist && chmod -R u+w src/Knit.Web/dist";
  preFixup = lib.optionalString aot "rm -f $out/lib/knit/*.dbg $out/lib/knit/*.pdb";
  executables = [ "knit" ];
  passthru = { inherit spa; };
  meta = {
    description = "Git-native task tracker";
    homepage = "https://github.com/MathiasReichardt/knit";
    license = lib.licenses.mit;
    mainProgram = "knit";
  };
}
