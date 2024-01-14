using Dalamud.Plugin.Services;
using Lumina.Data.Parsing;
using OtterGui;
using OtterGui.Tasks;
using Penumbra.Collections.Manager;
using Penumbra.GameData;
using Penumbra.GameData.Data;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Files;
using Penumbra.GameData.Structs;
using Penumbra.Import.Models.Export;
using Penumbra.Import.Models.Import;
using Penumbra.Import.Textures;
using Penumbra.Meta.Manipulations;
using SharpGLTF.Scenes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Penumbra.Import.Models;

using Schema2 = SharpGLTF.Schema2;
using LuminaMaterial = Lumina.Models.Materials.Material;

public sealed class ModelManager(IFramework framework, ActiveCollections collections, IDataManager gameData, GamePathParser parser, TextureManager textureManager) : SingleTaskQueue, IDisposable
{
    private readonly IFramework _framework          = framework;
    private readonly IDataManager _gameData         = gameData;
    private readonly TextureManager _textureManager = textureManager;

    private readonly ConcurrentDictionary<IAction, (Task, CancellationTokenSource)> _tasks = new();

    private bool _disposed;

    public void Dispose()
    {
        _disposed = true;
        foreach (var (_, cancel) in _tasks.Values.ToArray())
            cancel.Cancel();
        _tasks.Clear();
    }

    public Task ExportToGltf(MdlFile mdl, IEnumerable<string> sklbPaths, Func<string, byte[]> read, string outputPath)
        => Enqueue(new ExportToGltfAction(this, mdl, sklbPaths, read, outputPath));

    public Task<MdlFile?> ImportGltf(string inputPath)
    {
        var action = new ImportGltfAction(inputPath);
        return Enqueue(action).ContinueWith(task => 
        {
            if (task.IsFaulted && task.Exception != null)
                throw task.Exception;
            return action.Out;
        });
    }
    /// <summary> Try to find the .sklb paths for a .mdl file. </summary>
    /// <param name="mdlPath"> .mdl file to look up the skeletons for. </param>
    /// <param name="estManipulations"> Modified extra skeleton template parameters. </param>
    public string[] ResolveSklbsForMdl(string mdlPath, EstManipulation[] estManipulations)
    {
        var info = parser.GetFileInfo(mdlPath);
        if (info.FileType is not FileType.Model)
            return [];

        var baseSkeleton = GamePaths.Skeleton.Sklb.Path(info.GenderRace, "base", 1);

        return info.ObjectType switch
        {
            ObjectType.Equipment when info.EquipSlot.ToSlot() is EquipSlot.Body
                => [baseSkeleton, ..ResolveEstSkeleton(EstManipulation.EstType.Body, info, estManipulations)],
            ObjectType.Equipment when info.EquipSlot.ToSlot() is EquipSlot.Head
                => [baseSkeleton, ..ResolveEstSkeleton(EstManipulation.EstType.Head, info, estManipulations)],
            ObjectType.Equipment => [baseSkeleton],
            ObjectType.Accessory => [baseSkeleton],
            ObjectType.Character when info.BodySlot is BodySlot.Body or BodySlot.Tail => [baseSkeleton],
            ObjectType.Character when info.BodySlot is BodySlot.Hair
                => [baseSkeleton, ..ResolveEstSkeleton(EstManipulation.EstType.Hair, info, estManipulations)],
            ObjectType.Character when info.BodySlot is BodySlot.Face or BodySlot.Ear
                => [baseSkeleton, ..ResolveEstSkeleton(EstManipulation.EstType.Face, info, estManipulations)],
            ObjectType.Character => throw new Exception($"Currently unsupported human model type \"{info.BodySlot}\"."),
            ObjectType.DemiHuman => [GamePaths.DemiHuman.Sklb.Path(info.PrimaryId)],
            ObjectType.Monster   => [GamePaths.Monster.Sklb.Path(info.PrimaryId)],
            ObjectType.Weapon    => [GamePaths.Weapon.Sklb.Path(info.PrimaryId)],
            _                    => [],
        };
    }

    private string[] ResolveEstSkeleton(EstManipulation.EstType type, GameObjectInfo info, EstManipulation[] estManipulations)
    {
        // Try to find an EST entry from the manipulations provided.
        var (gender, race) = info.GenderRace.Split();
        var modEst = estManipulations
            .FirstOrNull(est => 
                est.Gender == gender
                && est.Race == race
                && est.Slot == type
                && est.SetId == info.PrimaryId
            );
        
        // Try to use an entry from provided manipulations, falling back to the current collection.
        var targetId = modEst?.Entry
            ?? collections.Current.MetaCache?.GetEstEntry(type, info.GenderRace, info.PrimaryId)
            ?? 0;

        // If there's no entries, we can assume that there's no additional skeleton.
        if (targetId == 0)
            return [];

        return [GamePaths.Skeleton.Sklb.Path(info.GenderRace, EstManipulation.ToName(type), targetId)];
    }

    /// <summary> Try to resolve the absolute path to a .mtrl from the potentially-partial path provided by a model. </summary>
    private string ResolveMtrlPath(string rawPath)
    {
        // TODO: this should probably be chosen in the export settings
        var variantId = 1;

        // Get standardised paths
        var absolutePath = rawPath.StartsWith('/')
            ? LuminaMaterial.ResolveRelativeMaterialPath(rawPath, variantId)
            : rawPath;
        var relativePath = rawPath.StartsWith('/')
            ? rawPath
            : '/' + Path.GetFileName(rawPath);
                    
        // TODO: this should be a recoverable warning
        if (absolutePath == null)
            throw new Exception("Failed to resolve material path.");
        
        var info = parser.GetFileInfo(absolutePath);
        if (info.FileType is not FileType.Material)
            throw new Exception($"Material path {rawPath} does not conform to material conventions.");

        var resolvedPath = info.ObjectType switch
        {
            ObjectType.Character => GamePaths.Character.Mtrl.Path(
                info.GenderRace, info.BodySlot, info.PrimaryId, relativePath, out _, out _, info.Variant),
            _ => absolutePath,
        };

        Penumbra.Log.Debug($"Resolved material {rawPath} to {resolvedPath}");

        return resolvedPath;
    }

    private Task Enqueue(IAction action)
    {
        if (_disposed)
            return Task.FromException(new ObjectDisposedException(nameof(ModelManager)));

        Task task;
        lock (_tasks)
        {
            task = _tasks.GetOrAdd(action, a =>
            {
                var token = new CancellationTokenSource();
                var t     = Enqueue(a, token.Token);
                t.ContinueWith(_ =>
                {
                    lock (_tasks)
                    {
                        return _tasks.TryRemove(a, out var unused);
                    }
                }, CancellationToken.None);
                return (t, token);
            }).Item1;
        }

        return task;
    }

    private class ExportToGltfAction(ModelManager manager, MdlFile mdl, IEnumerable<string> sklbPaths, Func<string, byte[]> read, string outputPath)
        : IAction
    {
        public void Execute(CancellationToken cancel)
        {
            Penumbra.Log.Debug($"[GLTF Export] Exporting model to {outputPath}...");

            Penumbra.Log.Debug("[GLTF Export] Reading skeletons...");
            var xivSkeletons = BuildSkeletons(cancel);

            Penumbra.Log.Debug("[GLTF Export] Reading materials...");
            var materials = mdl.Materials.ToDictionary(
                path => path,
                path => BuildMaterial(path, cancel)
            );

            Penumbra.Log.Debug("[GLTF Export] Converting model...");
            var model = ModelExporter.Export(mdl, xivSkeletons, materials);

            Penumbra.Log.Debug("[GLTF Export] Building scene...");
            var scene = new SceneBuilder();
            model.AddToScene(scene);

            Penumbra.Log.Debug("[GLTF Export] Saving...");
            var gltfModel = scene.ToGltf2();
            gltfModel.SaveGLTF(outputPath);
            Penumbra.Log.Debug("[GLTF Export] Done.");
        }

        /// <summary> Attempt to read out the pertinent information from the sklb file paths provided. </summary>
        private IEnumerable<XivSkeleton> BuildSkeletons(CancellationToken cancel)
        {
            var havokTasks = sklbPaths
                .Select(path => new SklbFile(read(path)))
                .WithIndex()
                .Select(CreateHavokTask)
                .ToArray();

            // Result waits automatically.
            return havokTasks.Select(task => SkeletonConverter.FromXml(task.Result));

            // The havok methods we're relying on for this conversion are a bit
            // finicky at the best of times, and can outright cause a CTD if they
            // get upset. Running each conversion on its own tick seems to make
            // this consistently non-crashy across my testing.
            Task<string> CreateHavokTask((SklbFile Sklb, int Index) pair) =>
                manager._framework.RunOnTick(
                    () => HavokConverter.HkxToXml(pair.Sklb.Skeleton),
                    delayTicks: pair.Index, cancellationToken: cancel);
        }

        /// <summary> Read a .mtrl and hydrate its textures. </summary>
        private MaterialExporter.Material BuildMaterial(string relativePath, CancellationToken cancel)
        {
            var path = manager.ResolveMtrlPath(relativePath);
            var mtrl = new MtrlFile(read(path));
            
            return new MaterialExporter.Material
            {
                Mtrl     = mtrl,
                Textures = mtrl.ShaderPackage.Samplers.ToDictionary(
                    sampler => (TextureUsage)sampler.SamplerId,
                    sampler => ConvertImage(mtrl.Textures[sampler.TextureIndex], cancel)
                ),
            };
        }

        /// <summary> Read a texture referenced by a .mtrl and convert it into an ImageSharp image. </summary>
        private Image<Rgba32> ConvertImage(MtrlFile.Texture texture, CancellationToken cancel)
        {
            // Work out the texture's path - the DX11 material flag controls a file name prefix.
            var texturePath = texture.Path;
            if (texture.DX11)
            {
                var lastSlashIndex = texturePath.LastIndexOf('/');
                var directory = lastSlashIndex == -1 ? texturePath : texturePath.Substring(0, lastSlashIndex);
                var fileName = Path.GetFileName(texturePath);
                if (!fileName.StartsWith("--"))
                {
                    texturePath = $"{directory}/--{fileName}";
                }
            }

            using var textureData = new MemoryStream(read(texturePath));
            var image = TexFileParser.Parse(textureData);
            var pngImage = TextureManager.ConvertToPng(image, cancel).AsPng;
            if (pngImage == null)
                throw new Exception("Failed to convert texture to png.");
            return pngImage;
        }

        public bool Equals(IAction? other)
        {
            if (other is not ExportToGltfAction rhs)
                return false;

            // TODO: compare configuration and such
            return true;
        }
    }

    private partial class ImportGltfAction(string inputPath) : IAction
    {
        public MdlFile? Out;

        public void Execute(CancellationToken cancel)
        {
            var model = Schema2.ModelRoot.Load(inputPath);

            Out = ModelImporter.Import(model);
        }

        public bool Equals(IAction? other)
        {
            if (other is not ImportGltfAction rhs)
                return false;

            return true;
        }
    }
}
