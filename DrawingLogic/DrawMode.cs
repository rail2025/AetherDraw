namespace AetherDraw.DrawingLogic
{
    public enum DrawMode
    {
        // Basic Shapes
        Pen,
        StraightLine,
        Rectangle,
        Circle,
        Arrow,
        Cone,
        Dash,
        Donut,
        Triangle,

        // Tools
        Select,
        Eraser,
        Image, // Added for generic downloaded images, if raidplan ever lets me download from them
        EmojiImage,

        // Image-based Tools / Placeables
        BossImage,
        CircleAoEImage,
        DonutAoEImage,
        FlareImage,
        LineStackImage,
        SpreadImage,
        StackImage,

        Waymark1Image,
        Waymark2Image,
        Waymark3Image,
        Waymark4Image,
        WaymarkAImage,
        WaymarkBImage,
        WaymarkCImage,
        WaymarkDImage,

        RoleTankImage,
        RoleHealerImage,
        RoleMeleeImage,
        RoleRangedImage,

        TriangleImage,
        SquareImage,
        PlusImage,
        CircleMarkImage,

        Party1Image,
        Party2Image,
        Party3Image,
        Party4Image,
        Party5Image,
        Party6Image,
        Party7Image,
        Party8Image,

        TextTool,

        // Other specific icons if they behave like placeable images
        StackIcon,
        SpreadIcon,
        TetherIcon,
        BossIconPlaceholder,
        AddMobIcon,
        Dot1Image,
        Dot2Image,
        Dot3Image,
        Dot4Image,
        Dot5Image,
        Dot6Image,
        Dot7Image,
        Dot8Image,

        StatusIconPlaceholder = 58,
    }
}
