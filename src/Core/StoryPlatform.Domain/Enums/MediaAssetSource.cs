namespace StoryPlatform.Domain.Enums;

public enum MediaAssetSource { AiGenerated = 0, UserUpload = 1, Unspecified = 2 }
public enum MediaTaskKind { GenerateImage = 0, GenerateChosenImage = 1, GenerateChosenAudio = 2, ValidateUploadedImage = 3, ValidateUploadedAudio = 4 }
