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

        // Tools
        Select,
        Eraser,

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

        TextTool,

        // Potentially other specific icons if they behave like placeable images
        StackIcon,        // If this is different from StackImage
        SpreadIcon,       // If this is different from SpreadImage
        TetherIcon,
        BossIconPlaceholder, // If this is different from BossImage
        AddMobIcon
    }
}
