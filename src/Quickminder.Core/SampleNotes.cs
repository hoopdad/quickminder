namespace Quickminder.Core;

public static class SampleNotes
{
    public static void SeedIfEmpty(NoteStore store)
    {
        if (store.List().Count != 0) return;

        store.Create("Everyday snippets\r\nThanks for the update!\r\nI'll take a look today.\r\nHave a great afternoon.\r\n\r\nFictional sample data.");
        store.Create("Links to keep close\r\nhttps://example.com docs\r\nhttps://example.org team\r\n\r\nCopy a line or open a link.\r\nFictional sample data.");
        store.Create("Project quick facts\r\nProject: Paper Plane\r\nStatus: Ready to review\r\nNext: Share the draft\r\n\r\nFictional sample data.");
    }
}
