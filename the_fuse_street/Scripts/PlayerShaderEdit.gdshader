shader_type canvas_item;

// Set this from code to the actual background colour (usually white)
uniform vec3 key_color : source_color = vec3(1.0, 1.0, 1.0);

// How close to key_color we treat as background
uniform float similarity : hint_range(0.0, 1.0) = 0.03;
uniform float smoothness : hint_range(0.0, 1.0) = 0.02;

// We only remove VERY desaturated pixels near key_color
uniform float max_bg_saturation : hint_range(0.0, 1.0) = 0.12;

void fragment()
{
    vec4 col = texture(TEXTURE, UV);

    // Colour distance to key_color
    float diff = distance(col.rgb, key_color);

    // Saturation
    float maxc = max(col.r, max(col.g, col.b));
    float minc = min(col.r, min(col.g, col.b));
    float sat  = (maxc - minc) / max(maxc, 0.0001);

    // If the pixel is too colourful, we DON'T treat it as background
    // i.e. shrink similarity when saturation is high
    float sat_factor = smoothstep(0.0, max_bg_saturation, sat);
    float effective_similarity = mix(similarity, 0.0, sat_factor); // colourful → similarity ~ 0

    // Smooth keying: transparent when diff ≈ 0, opaque when diff ≥ effective_similarity
    float alpha_factor = smoothstep(effective_similarity,
                                    effective_similarity + smoothness,
                                    diff);

    COLOR = vec4(col.rgb, col.a * alpha_factor);
}