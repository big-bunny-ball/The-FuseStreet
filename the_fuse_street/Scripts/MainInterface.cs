using System;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace TheFuseStreet.Scripts;

public partial class MainInterface : Control
{
    [Export] public TextEdit UserDesTextEdit;
    [Export] public LineEdit Culture1LineEdit;
    //public LineEdit Culture2LineEdit;
    [Export] public Button PlayButton;

    // http req nodes
    public HttpRequest TextRequest;
    public HttpRequest ImageRequest;

    // scene reference - find player, background, platform... in the sub-viewport
    public SubViewport SubViewport;
    
    const String WorldNodePath = "PanelContainer/HBoxContainer/VBoxContainer/SubViewportContainer/SubViewport/World";


    // openrouter api for current test
    private const String SiteRefer = "https://fusestreet.app";
    private const String TextModel = "google/gemini-3-pro-preview";
    private const String ImageModel = "google/gemini-3-pro-image-preview";
    private const String APIKey = "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";


    public override void _Ready()
    {
        TextRequest = GetNode<HttpRequest>("TextRequest");
        ImageRequest = GetNode<HttpRequest>("ImageRequest");
        SubViewport = GetNode<SubViewport>("PanelContainer/HBoxContainer/VBoxContainer/SubViewportContainer/SubViewport");

        PlayButton.Pressed += OnGeneratePressed;
    }


    private async void OnGeneratePressed()
    {
        GD.Print(">> starting generation pipeline... <<");
        PlayButton.Disabled = true;

        try
        {
            // step 1: generate visual description from gemini
            ArtDescriptionResponse artData = await GetArtDirectionFromGemini(
                UserDesTextEdit.Text.StripEdges(),
                Culture1LineEdit.Text.StripEdges()      
            ) ?? throw new Exception("failed to get art direction!");

            GD.Print($"BACKGROUND: {artData.background_prompt}");
            GD.Print($"PLAYER: {artData.player_visual}");
            GD.Print($"PLATFORM: {artData.platform_visual}");

            // step 2: generate player texture
            GD.Print("generating player sprite...");

            Texture2D playerTexture = await GenerateTexture(artData.player_visual, true, false);

            // not apply the texture if generation failed
            if (playerTexture != null) 
            {
                ApplyTextureToPlayer(playerTexture);
            }
            else
            {
                GD.PushError("player texture generation failed. Skipping!");
            }

            // step 3: generate background texture
            GD.Print("generating backgroud...");

            Texture2D backgroundTexture = await GenerateTexture(artData.background_prompt, false, false);
            if (backgroundTexture != null)
            {
                ApplyTextureToBackgrond(backgroundTexture);
            }

            // step 4: generate platform texture
            GD.Print("generating platform...");

            string platformPrompt = !string.IsNullOrEmpty(artData.platform_visual) 
                                    ? artData.platform_visual 
                                    : "Ground texture matching " + artData.background_prompt; // add a fall-back
            
            Texture2D platformTexture = await GenerateTexture(platformPrompt, false, true);
            if (platformTexture != null)
            {
                ApplyTextureToPlatform(platformTexture);
            }

            GD.Print("current generation pipeline done!");
        }
        catch (Exception e)
        {
            GD.PushError("pipeline ERROR! : " + e.Message);
        }
        finally
        {
            PlayButton.Disabled = false;
        }
    }

    // Text Generation
    private async Task<ArtDescriptionResponse> GetArtDirectionFromGemini(string userDescription, string cultureDefinition)
    {
        // system prompt
        String systemPrompt = @"
        You are the lead tech artist for a 2D side-scrolling platformer game.
        Your job is to convert the user's idea and culture description into three short PROMPTS
        for an image generator: one for the BACKGROUND, one for the PLAYER, and one for the PLATFORM.

        You must always follow these rules:

        1) GLOBAL STYLE (applies to all three prompts)
        - Stylised 2D game art inspired by pixel art / retro CG platformers:
        - sharp, blocky shading, clear outlines, no soft painterly gradients.
        - visible but clean pixel structure or crisp comic-like inking.
        - Everything is in focus: no depth-of-field blur, no soft-focus smearing, no photographic bokeh.
        - Use a consistent art style, colour palette and lighting across BACKGROUND, PLAYER and PLATFORM.

        2) BACKGROUND (""background_prompt"")
        - Goal: the main side-view environment layer behind the ground where the player runs,
        like the street / ruins / forest edge seen in classic 2D platformers.
        - Camera: side view, medium distance; not an extreme close-up wall, not a tiny far-away skyline.
        - Horizontal framing: a wide continuous scene that can scroll horizontally.

        - Vertical composition:
        - Bottom 10-15% of the image:
            - Simple band that continues the colour of the street or floor behind the gameplay platform.
            - Little or no detail here; this area may be partially covered by the platform sprite.
        - From about 15% up to about 60-65% of the image height:
            - The main row of mid-distance environment forms that match the culture:
            - e.g. building facades, doors, windows, arches, balconies, shop fronts, signs, pipes, columns,
                tree trunks, rock faces, ruins, etc., depending on the setting.
            - Scale elements so they look believable relative to a human-sized character in a platformer.
            - The tallest elements must stay clearly below the top of the image so that a sky band remains visible.
        - Top 25-35% of the image:
            - Clear sky or atmospheric gradient appropriate to the scene (or interior ceiling if indoors),
            optionally with a softer distant skyline or silhouettes.
            - No buildings or objects should touch the very top edge of the image.

        - Style:
        - Stylised 2D game art with crisp pixel-art-like blocks or clean inked line art,
            similar sharpness to the player sprite.
        - Medium detail: enough features to recognise the culture and setting, but avoid noisy micro-texture.
        - Do NOT generate a single flat close-up wall/door that fills the frame.
        - Do NOT generate only a far skyline with almost empty mid-ground.

        3) PLAYER (""player_visual"")
        - Goal: a small spritesheet for a side-scrolling character.
        - Style: pixel-art-inspired or sharp cel-style 2D:
        - clear outline, strong readable silhouette, limited but harmonious colour palette.
        - Background:
        - Pure white (#FFFFFF) background only, same for all frames.
        - No ground, no shadow, no props, no other people, no horizon, no gradient.

        - Animation layout:
        - Create a single image containing ANIMATION FRAMES arranged in rows:
            - Row 1: idle or standing pose, 4 frames.
            - Row 2: running cycle, 6 frames.
        - All frames are side view, facing right, full body visible, same size, aligned to the same baseline.
        - Leave small even gaps between frames if needed, but keep grid regular.

        - Character concept:
        - Use the user's culture/setting to design outfit, colours and accessories,
            but always keep the character readable at small sprite size.

        4) PLATFORM (""platform_visual"")
        - Goal: a ground material texture the player runs on.
        - A single seamless square tile viewed from straight top-down (orthographic).
        - The surface must look like walkable ground: dirt, sand, stone, grass, or an equivalent material
        that fits the culture.
        - Style: pixel-art-like or sharp CG:
        - clear but simple tonal blocks, not noisy realism.
        - Very low contrast, soft mottled detail; the ground should not draw more attention than the player.
        - Avoid: bricks, checkerboards, mosaics, decorative borders, or anything that looks like a wall or carpet.
        - Useful phrases to include: ""seamless texture"", ""ground tile"", ""top-down view"",
        ""flat lighting"", ""soft irregular mottled surface"", ""subtle variation"", ""walkable ground"".

        OUTPUT FORMAT:
        Return ONLY a single valid JSON object. Do not add explanations, comments, or markdown fences.
        The JSON must have exactly this structure:

        {
        ""background_prompt"": ""..."",
        ""player_visual"": ""..."",
        ""platform_visual"": ""...""
        }
        ";

        // user prompt
        String userPrompt =
        "User idea: " + userDescription +
        ". Culture or setting: " + cultureDefinition +
        ". Use the structure of classic 2D platformer scenes: a side-view environment band behind a ground line," +
        " with mid-distance facades or landscape in the middle of the image and a clear sky band above." +
        " The style should be sharp pixel-art-inspired 2D game art, with no blur." +
        " The player output is a small side-view spritesheet on pure white, and the platform is a top-down seamless ground tile." +
        " Follow the system rules exactly and respond with JSON only.";


        var requestBody = new OpenRouterRequest
        {
            model = TextModel,
            messages =
            [
                new OpenRouterMessage { role = "system", content = systemPrompt },
                new OpenRouterMessage { role = "user",   content = userPrompt   }
            ]
        };

        string jsonResponse = await PostRequest(TextRequest, JsonSerializer.Serialize(requestBody));

        using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
        {
            string contentString = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            
            
            // handle possible null content from JSON
            if (string.IsNullOrWhiteSpace(contentString))
            {
                GD.PushError("Text generation: 'content' field was null or empty.");
                return null;
            }

            // strip possible ```json fences just in case
            contentString = contentString
                .Replace("```json", string.Empty)
                .Replace("```", string.Empty)
                .Trim();

            return JsonSerializer.Deserialize<ArtDescriptionResponse>(contentString);
        }
    }


    // image generation
    private async Task<Texture2D> GenerateTexture(String prompt, bool isCharacter, bool isPlatform)
    {
        // compute ratio directly
        String ratio = isCharacter ? "9:16" : isPlatform ? "1:1" : "21:9";

        // payload for nano banana
        var reqBody = new
        {
            model = ImageModel,
            messages = new[]
            {
                new {role = "user", content = prompt}
            },
            image_config = new
            {
                aspect_ratio = ratio
            }
        };

        String jsonResponse = await PostRequest(ImageRequest, JsonSerializer.Serialize(reqBody));

        // safety check for request
        if (String.IsNullOrEmpty(jsonResponse))
        {
            GD.PushError("image generation: API returned null.");
            return null;
        }

        // extract URL from JSON
        using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
        {
            // referring to OpenRouter's API documentation for image generation
            var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

            // CRITICAL CHECK if "images" exist (safety filter causes it to disappear)
            if (message.TryGetProperty("images", out JsonElement imagesArray))
            {
                // according to python reference: images[0] -> image_url -> url
                String imageUrl = imagesArray[0]
                    .GetProperty("image_url")
                    .GetProperty("url")
                    .GetString();
                
                // download image
                return await DownloadImage(imageUrl);
            }
            
            String content = message.GetProperty("content").GetString();
            GD.PushError($"Gemini Refused to generate image. Reason: {content}");
            return null;
            
        }
        
    }


    // helper: SEND JPPT REQUEST
    private async Task<String> PostRequest(HttpRequest http, String jsonBody)
    {
        // headers
        String[] headers = [
            "Content-Type: application/json",
            $"Authorization: Bearer {APIKey}",
            $"HTTP-Referer: {SiteRefer}"
        ];

        // send the request
        const String openrouterUrl = "https://openrouter.ai/api/v1/chat/completions";
        Error err = http.Request(openrouterUrl, headers, HttpClient.Method.Post, jsonBody);
        if (err != Error.Ok)
        {
            GD.PushError("error: Request failed to start");
            return null;
        }

        var result = await ToSignal(http, HttpRequest.SignalName.RequestCompleted);
        
        // on http request completed
        long responseCode = (long)result[1];
        byte[] body = (byte[])result[3];

        if (responseCode == 200)
        {
            return System.Text.Encoding.UTF8.GetString(body);
        }
        else
        {
            GD.PushError("Request Failed! Error code: " + responseCode.ToString());
            GD.Print(System.Text.Encoding.UTF8.GetString(body));
            return null;
        }
    }


    // modified helper: handle both URLs (https://) and Base64 Data (data:image/...)
    private async Task<Texture2D> DownloadImage(String imageData)
    {
        // CASE 1: AI returned the image directly as Base64 text
        if (imageData.StartsWith("data:image"))
        {
            GD.Print("Image is Base64 encoded. Converting directly...");
            
            // strip the header ("data:image/jpeg;base64,")
            // use char overload to avoid culture-specific warning
            int commaIndex = imageData.IndexOf(',');
            if (commaIndex < 0)
            {
                GD.PushError("Base64 image data did not contain a comma separator.");
                return null;
            }

            string pureBase64 = imageData.Substring(commaIndex + 1);

            byte[] imageBytes = Convert.FromBase64String(pureBase64);
            
            // select the loader based on the header (jpeg or png)
            Image img = new Image();
            Error err;
            
            if (imageData.Contains("image/png"))
            {
                err = img.LoadPngFromBuffer(imageBytes);
            }
            else
            {
                err = img.LoadJpgFromBuffer(imageBytes);
            }

            if (err == Error.Ok)
            {
                return ImageTexture.CreateFromImage(img);
            }
            else 
            {
                GD.PushError("Failed to convert Base64 image to Texture.");
                return null;
            }
        }

        // CASE 2: AI returned a web link (https://...) - Use HTTP Request
        HttpRequest downloader = new HttpRequest();
        AddChild(downloader);

        Error reqErr = downloader.Request(imageData);
        if (reqErr != Error.Ok)
        {
            GD.PushError(string.Concat("Error: Failed to start image downloading. URL was: ", imageData.AsSpan(0, 20), "..."));
            downloader.QueueFree();
            return null;
        }

        var result = await ToSignal(downloader, HttpRequest.SignalName.RequestCompleted);
        downloader.QueueFree();

        long respCode = (long)result[1];
        byte[] body = (byte[])result[3];

        if (respCode == 200)
        {
            Image img = new Image();
            Error loadErr = img.LoadPngFromBuffer(body);
            if (loadErr != Error.Ok)
            {
                loadErr = img.LoadJpgFromBuffer(body);
            }

            if (loadErr == Error.Ok)
            {
                return ImageTexture.CreateFromImage(img);
            }
        }

        GD.PushWarning("Image Download Failed! Code: " + respCode.ToString());
        return null;
    }


    // helper: apply texture to player
    private void ApplyTextureToPlayer(Texture2D PlayerTexture)
    {
        var player = SubViewport.GetNode<Player>("World/Player");
        player.UpdateVisuals(PlayerTexture);
    }

    // helper: apply texture to background
    private void ApplyTextureToBackgrond(Texture2D BackgroundTexture)
    {
        TextureRect Background = GetNode<TextureRect>(WorldNodePath + "/StaticBody2DBackgroud/Background");
        Background.Texture = BackgroundTexture;
        Background.StretchMode = TextureRect.StretchModeEnum.Scale;
        Background.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
    }


    // helper: apply texture to platform
    private void ApplyTextureToPlatform(Texture2D PlatformTexture)
    {
        // get the raw image data
        Image img = PlatformTexture.GetImage();

        // resize it for better tiling
        img.Resize(256, 256, Image.Interpolation.Lanczos);
        // create a new smaller texture for tiling
        ImageTexture tilingTexture = ImageTexture.CreateFromImage(img);

        TextureRect Platform = GetNode<TextureRect>(WorldNodePath + "/StaticBody2DPlatform/Platform");
        Platform.Texture = tilingTexture;
        Platform.StretchMode = TextureRect.StretchModeEnum.Tile;
        Platform.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
    }


}

//-------------------------------
// DATA STRUCTURE FOR JSON PARSING
//--------------------------------

// structure sent to openrouter
public class OpenRouterRequest
{
    public String model { get; set; }
    public OpenRouterMessage[] messages { get; set; }
}

public class OpenRouterMessage
{
    public String role { get; set; }
    public String content { get; set; }
}

// structure received from Gemini 3 pro (TEXT-JSONDATA)
public class ArtDescriptionResponse
{
    public String background_prompt { get; set; }
    public String player_visual { get; set; }
    public String platform_visual { get; set; }
    
    // add FUSION and NPC visual Later
}

// structure received from Nano Banana pro (IMAGE)
public class NanoBananaResponse
{
    public Choice[] choices { get; set; }
    public class Choice {public Message message { get; set; } }
    public class Message
    {
        public String content { get; set; }
        public ImageURL[] images { get; set; }
    }
    public class ImageURL {public URL url { get; set; }}
    public class URL {public String url { get; set; }}
}