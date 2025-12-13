using System;
using System.Data;
using System.Reflection;
using System.Runtime.Intrinsics.X86;
using Godot;
using System.Collections.Generic;

namespace TheFuseStreet.Scripts;

public partial class Player : CharacterBody2D
{
	[ExportCategory("Movement Settings")]
    [Export] public float RunSpeed = 500.0f;
    [Export] public float JumpVelocity = -500.0f;
    [Export] public float JumpGravity = 1500.0f;
    [Export] public float FallGravity = 2500.0f;

    [ExportCategory("Visual Import Settings")]
    // Target height in pixels for the player in-game
    [Export] public float TargetVisualHeight = 220.0f;

    // --- SAFETY SETTINGS ---
    // If true, the code counts pixels to find columns. If false, it uses the numbers below.
    [Export] public bool UseAutoDetection = true;
    
    // Fallback numbers if auto-detection is off
    [Export] public int ManualIdleColumns = 4;
    [Export] public int ManualRunColumns = 6;

    // How much of the cell to keep? 0.4 = 40% (Very Safe). 0.8 = 80% (Risk of ghosting).
    [Export(PropertyHint.Range, "0.2, 0.9")] public float CropSafetyFactor = 0.40f;

    // Internal Nodes
    public AnimatedSprite2D VisualSprite;
    private ColorRect PlaceHolder;
    private bool _animationsReady = false;
	private int jumpCount = 0;


    public override void _Ready()
    {
        VisualSprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
		PlaceHolder = GetNode<ColorRect>("ColorRect");

		_animationsReady = false;
    }


    public void UpdateVisuals(Texture2D newTextures)
	{
		if (newTextures == null)
		{
			GD.PushWarning("[Player] UpdateVisuals called with null texture.");
			return;
		}

		Image img = newTextures.GetImage();
		if (img == null)
		{
			GD.PushWarning("[Player] Texture.GetImage() returned null.");
			return;
		}

		int imgW = img.GetWidth();
		int imgH = img.GetHeight();
		if (imgW <= 0 || imgH <= 0)
		{
			GD.PushWarning($"[Player] Invalid texture size: {imgW}x{imgH}");
			return;
		}

		// We assume 2 logical rows: idle on top, run on bottom.
		const int rows = 2;
		if (imgH < rows)
		{
			GD.PushWarning($"[Player] Texture height {imgH} too small for {rows} rows.");
			return;
		}

		// ----- 1. Determine column counts -----

		int idleCols = Math.Max(1, ManualIdleColumns);
		int runCols  = Math.Max(1, ManualRunColumns);

		Color bg = img.GetPixel(0, 0);

		if (UseAutoDetection)
		{
			int yIdle = ClampInt((int)(imgH * 0.25f), 0, imgH - 1);
			int yRun  = ClampInt((int)(imgH * 0.75f), 0, imgH - 1);

			int detectedIdle = DetectSpriteCount(img, yIdle, bg);
			if (detectedIdle > 0) idleCols = detectedIdle;

			int detectedRun = DetectSpriteCount(img, yRun, bg);
			if (detectedRun > 0) runCols = detectedRun;
			else runCols = idleCols; // fallback

			idleCols = Math.Max(1, idleCols);
			runCols  = Math.Max(1, runCols);

			GD.Print($"[Player] Auto-Detected Layout: Idle={idleCols} cols, Run={runCols} cols.");
		}

		// ----- 2. Slice into raw frames (grid-based) -----

		int rowHeight = imgH / rows;
		if (rowHeight <= 0)
		{
			GD.PushWarning("[Player] Computed rowHeight is 0.");
			return;
		}

		// Local container for all frames before normalization
		var rawFrames = new List<FrameData>();
		int maxContentW = 1;
		int maxContentH = 1;

		// Helper to process one row (idle or run)
		void AddRowFrames(string animName, int cols, int rowIndex)
		{
			if (cols <= 0) return;

			int cellW = Math.Max(1, imgW / cols);
			int rowY  = rowIndex * rowHeight;

			for (int i = 0; i < cols; i++)
			{
				int startX = i * cellW;
				Rect2I region = ClampRect(new Rect2I(startX, rowY, cellW, rowHeight), imgW, imgH);

				Image cellImg = img.GetRegion(region);

				// >>> IMPORTANT: force a consistent format <<<
				if (cellImg.GetFormat() != Image.Format.Rgba8)
					cellImg.Convert(Image.Format.Rgba8);

				bool preferUpper = animName == "idle"; // idle uses upper band, run uses lower band

				if (!TryGetMainContentRect(cellImg, bg, preferUpper, out Rect2I contentRect))
				{
					// Entire cell looked like background; skip this frame.
					continue;
				}

				int cw = contentRect.Size.X;
				int ch = contentRect.Size.Y;
				if (cw <= 0 || ch <= 0)
					continue;

				maxContentW = Math.Max(maxContentW, cw);
				maxContentH = Math.Max(maxContentH, ch);

				rawFrames.Add(new FrameData
				{
					AnimName    = animName,
					FrameIndex  = i,
					SourceImage = cellImg,
					ContentRect = contentRect
				});
			}
		}

		// Collect idle (row 0) & run (row 1)
		AddRowFrames("idle", idleCols, 0);
		AddRowFrames("run",  runCols,  1);

		if (rawFrames.Count == 0)
		{
			GD.PushWarning("[Player] No frames detected in spritesheet.");
			return;
		}

		// Add some small padding for safety
		int finalFrameW = maxContentW + 4;
		int finalFrameH = maxContentH + 4;

		// ----- 3. Build normalized SpriteFrames -----

		SpriteFrames frames = new SpriteFrames();

		frames.AddAnimation("idle");
		frames.SetAnimationLoop("idle", true);
		frames.SetAnimationSpeed("idle", 6f);

		frames.AddAnimation("run");
		frames.SetAnimationLoop("run", true);
		frames.SetAnimationSpeed("run", 12f);

		frames.AddAnimation("jump");
		frames.SetAnimationLoop("jump", false);

		// Keep track of how many frames we actually add per anim
		int idleFrameCount = 0;
		int runFrameCount  = 0;

		foreach (var rf in rawFrames)
		{
			// Create an aligned, common-sized image
			Image outImg = Godot.Image.CreateEmpty(finalFrameW, finalFrameH, false, Image.Format.Rgba8);
			outImg.Fill(new Color(0, 0, 0, 0)); // fully transparent
			

			// Place content bottom-aligned and horizontally centered
			int destX = (finalFrameW - rf.ContentRect.Size.X) / 2;
			int destY = finalFrameH - rf.ContentRect.Size.Y;

			destX = ClampInt(destX, 0, finalFrameW - rf.ContentRect.Size.X);
			destY = ClampInt(destY, 0, finalFrameH - rf.ContentRect.Size.Y);

			outImg.BlitRect(rf.SourceImage, rf.ContentRect, new Vector2I(destX, destY));

			ImageTexture tex = ImageTexture.CreateFromImage(outImg);

			if (rf.AnimName == "idle")
			{
				frames.AddFrame("idle", tex);
				idleFrameCount++;
			}
			else if (rf.AnimName == "run")
			{
				frames.AddFrame("run", tex);
				runFrameCount++;
			}
		}

		if (idleFrameCount == 0 && runFrameCount > 0)
		{
			// Fallback: copy first run frame as idle
			Texture2D t = frames.GetFrameTexture("run", 0);
			frames.AddFrame("idle", t);
			idleFrameCount = 1;
		}

		if (runFrameCount == 0 && idleFrameCount > 0)
		{
			// Fallback: copy first idle frame as run
			Texture2D t = frames.GetFrameTexture("idle", 0);
			frames.AddFrame("run", t);
			runFrameCount = 1;
		}

		// Jump: reuse a mid‑run frame if available
		if (runFrameCount > 0)
		{
			int jumpIndex = Mathf.Clamp(1, 0, runFrameCount - 1);
			Texture2D jumpTex = frames.GetFrameTexture("run", jumpIndex);
			frames.AddFrame("jump", jumpTex);
		}

		// ----- 4. Apply to sprite and position -----

		VisualSprite.SpriteFrames = frames;

		// Scale: use the unified finalFrameH so all anims match
		float scaleRatio = TargetVisualHeight / (float)finalFrameH;
		VisualSprite.Scale = new Vector2(scaleRatio, scaleRatio);

		// Feet on floor: bottom of frame touches "ground"
		VisualSprite.Position = new Vector2(0, -TargetVisualHeight / 2.0f);

		if (VisualSprite.Material is ShaderMaterial mat)
		{
			mat.SetShaderParameter("key_color", bg);
		}

		VisualSprite.Play("idle");

		_animationsReady = true;
		PlaceHolder?.Hide();
	}

	/// <summary>
	/// Data about one raw frame before normalization.
	/// </summary>
	private struct FrameData
	{
		public string AnimName;
		public int FrameIndex;
		public Image SourceImage;
		public Rect2I ContentRect;
	}

	/// <summary>
	/// Returns true and a tight bounding box of non‑background pixels, or false if empty.
	/// </summary>
	// Find the main content band in the image.
// - preferUpper = true  => use the highest band with content (for idle)
// - preferUpper = false => use the lowest band with content (for run)
	private static bool TryGetMainContentRect(Image img, Color bg, bool preferUpper, out Rect2I contentRect)
	{
		int w = img.GetWidth();
		int h = img.GetHeight();

		if (w <= 0 || h <= 0)
		{
			contentRect = default;
			return false;
		}

		// 1. For each row, check if it has any content pixels
		bool[] rowHasContent = new bool[h];

		for (int y = 0; y < h; y++)
		{
			bool any = false;
			for (int x = 0; x < w; x++)
			{
				if (IsContentPixel(img.GetPixel(x, y), bg))
				{
					any = true;
					break;
				}
			}
			rowHasContent[y] = any;
		}

		// 2. Build vertical bands of consecutive content rows
		var bands = new List<(int Start, int End)>();
		int currentStart = -1;

		for (int y = 0; y < h; y++)
		{
			if (rowHasContent[y])
			{
				if (currentStart == -1)
					currentStart = y;
			}
			else if (currentStart != -1)
			{
				bands.Add((currentStart, y - 1));
				currentStart = -1;
			}
		}

		if (currentStart != -1)
			bands.Add((currentStart, h - 1));

		if (bands.Count == 0)
		{
			contentRect = default;
			return false;
		}

		// 3. Choose uppermost or lowermost band
		(int Start, int End) chosenBand =
			preferUpper ? bands[0] : bands[bands.Count - 1];

		// 4. Within that band, find min/max X of content
		int minX = w;
		int maxX = -1;

		for (int y = chosenBand.Start; y <= chosenBand.End; y++)
		{
			for (int x = 0; x < w; x++)
			{
				if (!IsContentPixel(img.GetPixel(x, y), bg))
					continue;

				if (x < minX) minX = x;
				if (x > maxX) maxX = x;
			}
		}

		if (maxX < minX)
		{
			contentRect = default;
			return false;
		}

		contentRect = new Rect2I(
			minX,
			chosenBand.Start,
			maxX - minX + 1,
			chosenBand.End - chosenBand.Start + 1
		);

		return true;
	}


	private static bool IsContentPixel(Color c, Color bg)
	{
		// Ignore fully/mostly transparent
		if (c.A < 0.05f) return false;

		float diff =
			Math.Abs(c.R - bg.R) +
			Math.Abs(c.G - bg.G) +
			Math.Abs(c.B - bg.B);

		return diff > 0.10f;
	}


	// Re‑used from before: scanline‑based column detection
	private static int DetectSpriteCount(Image img, int scanY, Color bg)
	{
		int w = img.GetWidth();
		int h = img.GetHeight();
		if (w <= 0 || h <= 0) return 0;

		scanY = ClampInt(scanY, 0, h - 1);

		int count = 0;
		bool inside = false;
		int gap = 0;
		const int minGap = 6;

		for (int x = 0; x < w; x++)
		{
			Color c = img.GetPixel(x, scanY);
			bool content = IsContentPixel(c, bg);

			if (content)
			{
				if (!inside)
				{
					count++;
					inside = true;
				}
				gap = 0;
			}
			else if (inside)
			{
				gap++;
				if (gap >= minGap)
					inside = false;
			}
		}

		return count;
	}


	private static Rect2I ClampRect(Rect2I r, int maxW, int maxH)
	{
		if (maxW <= 0 || maxH <= 0)
			return new Rect2I(0, 0, 1, 1);

		int x = ClampInt(r.Position.X, 0, Math.Max(0, maxW - 1));
		int y = ClampInt(r.Position.Y, 0, Math.Max(0, maxH - 1));

		int w = r.Size.X;
		int h = r.Size.Y;

		if (w <= 0) w = 1;
		if (h <= 0) h = 1;

		if (x + w > maxW) w = maxW - x;
		if (y + h > maxH) h = maxH - y;

		if (w <= 0) w = 1;
		if (h <= 0) h = 1;

		return new Rect2I(x, y, w, h);
	}


	private static int ClampInt(int value, int min, int max)
	{
		if (value < min) return min;
		if (value > max) return max;
		return value;
	}


	public override void _PhysicsProcess(double delta)
	{
		Vector2 velocity = Velocity;
		//RunSpeed = 300.0f;
		//JumpVelocity = -400.0f;

		// add the gravity
		if (!IsOnFloor())
		{
			if (velocity.Y > 0) velocity.Y += FallGravity * (float)delta;
			else velocity.Y += JumpGravity * (float)delta;
		}
		else
		{
			jumpCount = 0;
		}

		// handle Jump
		int maxJump = 2;

		if (Input.IsActionJustPressed("ui_accept"))
		{
			if (IsOnFloor() || jumpCount < maxJump)
			{
                velocity.Y = JumpVelocity;
                jumpCount += 1;
			}
        }

        Vector2 direction = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");

		if (direction != Vector2.Zero)
		{
			velocity.X = direction.X * RunSpeed;
		}
		else
		{
			velocity.X = Mathf.MoveToward(Velocity.X, 0, RunSpeed);
		}

		Velocity = velocity;
		MoveAndSlide();

		// --- ANIMATION & FACING ---
		if (!_animationsReady)
		{
			return; // spritesheet are not generate yet
		}

		// Flip when moving
		if (direction.X != 0)
		{
			VisualSprite.FlipH = direction.X < 0;
		}

		// If we don't have any generated animations yet, skip animation logic
		var frames = VisualSprite.SpriteFrames;
		if (frames == null)
			return;

		string targetAnim;

		if (!IsOnFloor())
		{
			targetAnim = "jump";
		}
		else if (Mathf.Abs(velocity.X) > 5f)
		{
			targetAnim = "run";
		}
		else
		{
			targetAnim = "idle";
		}

		// Only play if that animation actually exists
		if (frames.HasAnimation(targetAnim))
		{
			if (VisualSprite.Animation != targetAnim)
				VisualSprite.Play(targetAnim);
		}

	}


		// ORIGINAL STATIC SPRITE ONLY

		/* VisualSprite.Texture = newTextures;

		// optional: resize the sprite to fit the collider no matter how big the image is
        float targetHeight = 200.0f; // collider height?
        float scale = targetHeight / newTextures.GetHeight();
        VisualSprite.Scale = new Vector2(scale, scale);

		// --- NEW: set key_color from texture corner ---
    	var img = newTextures.GetImage();
    	if (img != null)
		{
			Color c1 = img.GetPixel(0, 0);
			Color c2 = img.GetPixel(img.GetWidth() - 1, 0);
			Color c3 = img.GetPixel(0, img.GetHeight() - 1);
			Color c4 = img.GetPixel(img.GetWidth() - 1, img.GetHeight() - 1);

			Color avg = (c1 + c2 + c3 + c4) / 4.0f;

			if (VisualSprite.Material is ShaderMaterial mat)
			{
				mat.SetShaderParameter("key_color", avg);
			}
		}

		PlaceHolder.Hide(); */
}
