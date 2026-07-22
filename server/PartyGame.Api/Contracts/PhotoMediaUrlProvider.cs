namespace PartyGame.Api.Contracts;

public interface IPhotoMediaUrlProvider
{
    string Display(Guid mediaAssetId);
    string Thumbnail(Guid mediaAssetId);
}

public sealed class PhotoMediaUrlProvider : IPhotoMediaUrlProvider
{
    public string Display(Guid mediaAssetId) => $"/api/media/{mediaAssetId:D}/display";
    public string Thumbnail(Guid mediaAssetId) => $"/api/media/{mediaAssetId:D}/thumbnail";
}
