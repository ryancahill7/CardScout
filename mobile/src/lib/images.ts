import { ImageManipulator, SaveFormat } from 'expo-image-manipulator';

const MAX_DIMENSION = 1600;

// Shrinks a photo so its longest side is at most 1600px and re-encodes it as JPEG.
// Keeps uploads small and fast without losing the detail needed to read card text.
export async function prepareForUpload(uri: string, width: number, height: number): Promise<string> {
  const context = ImageManipulator.manipulate(uri);
  if (Math.max(width, height) > MAX_DIMENSION) {
    if (width >= height) context.resize({ width: MAX_DIMENSION, height: null });
    else context.resize({ width: null, height: MAX_DIMENSION });
  }
  const rendered = await context.renderAsync();
  const result = await rendered.saveAsync({ format: SaveFormat.JPEG, compress: 0.8 });
  return result.uri;
}
