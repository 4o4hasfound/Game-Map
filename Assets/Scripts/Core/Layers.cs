// Physics layers (set up by TapeBuilder.Setup).
public static class Layers
{
    public const int Player = 8, Monster = 9, Structure = 10, Door = 11, Prop = 12, Ceiling = 13, Interactable = 14, Avatar = 15, Hiding = 16;
    public const int Blockers = (1 << Structure) | (1 << Door) | (1 << Prop) | (1 << Ceiling);
}
