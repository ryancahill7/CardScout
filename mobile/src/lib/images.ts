import { ImageManipulator, SaveFormat } from 'expo-image-manipulator';

const DEFAULT_MAX_DIMENSION = 1600;

// Shrinks a photo so its longest side is at most maxDimension (1600px by default) and re-encodes it as JPEG.
// Keeps uploads small and fast without losing the detail needed to read card text.
export async function prepareForUpload(
  uri: string,
  width: number,
  height: number,
  maxDimension = DEFAULT_MAX_DIMENSION,
): Promise<string> {
  const context = ImageManipulator.manipulate(uri);
  if (Math.max(width, height) > maxDimension) {
    if (width >= height) context.resize({ width: maxDimension, height: null });
    else context.resize({ width: null, height: maxDimension });
  }
  const rendered = await context.renderAsync();
  const result = await rendered.saveAsync({ format: SaveFormat.JPEG, compress: 0.8 });
  return result.uri;
}
