namespace TimonMigrations.Migrations

open FluentMigrator

[<Migration(202009262231L, "CreateClubLinkTags")>]
type CreateClubLinkTags() =
    inherit Migration()

    override this.Up() =
        this
            .Create
            .Table("ClubLinkTags")
            .WithColumn("ClubId")
            .AsGuid()
            .ForeignKey("Clubs", "Id")
            .WithColumn("TagName")
            .AsString()
            .ForeignKey("Tags", "Name")
            .WithColumn("LinkId")
            .AsGuid()
            .ForeignKey("Links", "Id")
            .WithColumn("Count")
            .AsInt64()
            .WithDefaultValue(0)
        |> ignore

        this
            .Create
            .PrimaryKey("ClubLinkTags_Composite")
            .OnTable("ClubLinkTags")
            .Columns("TagName", "LinkId", "ClubId")
        |> ignore

    override this.Down() =
        this.Delete.Table("ClubLinkTags")
        |> ignore
