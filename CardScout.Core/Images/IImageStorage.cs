namespace CardScout.Core.Images;

// Paths returned by SaveAsync are storage-relative keys, so swapping to blob storage
// later doesn't require rewriting what's stored in CARD_INVENTORY.
public interface IImageStorage
{
    // folder groups an owner's images (e.g. "users/42") so they can be cleaned up or moved together
    Task<string> SaveAsync(Stream content, string extension, string folder);
    Task<Stream?> OpenReadAsync(string path);
    Task DeleteAsync(string path);
    // Removes everything under a folder (account deletion)
    Task DeleteFolderAsync(string folder);
}
