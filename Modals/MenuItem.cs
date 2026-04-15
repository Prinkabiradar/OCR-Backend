namespace OCR_BACKEND.Modals
{
    public class MenuItem
    {
        public int MenuId { get; set; }
        public string Title { get; set; }
        public string Route { get; set; }
        public string? Icon { get; set; }
        public int? ParentId { get; set; }
        public bool CanView { get; set; }
        public bool CanInsert { get; set; }
        public bool CanUpdate { get; set; }
        public bool CanDelete { get; set; }
        public List<MenuItem> SubMenu { get; set; } = new List<MenuItem>();
    }
}
