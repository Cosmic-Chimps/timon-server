namespace TimonMigrations.Migrations

open FluentMigrator

[<Migration(202008091339L, "CreateInitial")>]
type InitialMigration() =
    inherit Migration()

    override this.Up() =
        this
            .Create
            .Table("Users")
            .WithColumn("Id")
            .AsGuid()
            .PrimaryKey()
            .WithColumn("DisplayName")
            .AsString()
        |> ignore


        this
            .Create
            .Table("Clubs")
            .WithColumn("Id")
            .AsGuid()
            .PrimaryKey()
            .WithDefault(SystemMethods.NewGuid)
            .WithColumn("Name")
            .AsString()
            .NotNullable()
            .WithColumn("DateCreated")
            .AsDateTimeOffset()
            .WithDefault(SystemMethods.CurrentUTCDateTime)
        |> ignore

        this
            .Create
            .Table("ClubUsers")
            .WithColumn("ClubId")
            .AsGuid()
            .ForeignKey("Clubs", "Id")
            .WithColumn("UserId")
            .AsGuid()
            .ForeignKey("Users", "Id")
            .WithColumn("DateCreated")
            .AsDateTimeOffset()
            .WithDefault(SystemMethods.CurrentUTCDateTime)
        |> ignore

        this
            .Create
            .Table("Links")
            .WithColumn("Id")
            .AsGuid()
            .PrimaryKey()
            .WithDefault(SystemMethods.NewGuid)
            .WithColumn("Url")
            .AsString()
            .NotNullable()
            .WithColumn("DomainName")
            .AsString()
            .NotNullable()
            .WithColumn("Title")
            .AsString()
            .NotNullable()
            .WithColumn("ShortDescription")
            .AsString()
            .NotNullable()
            .WithColumn("DateCreated")
            .AsDateTimeOffset()
            .WithDefault(SystemMethods.CurrentUTCDateTime)
        |> ignore

        this
            .Create
            .Table("Channels")
            .WithColumn("Id")
            .AsGuid()
            .PrimaryKey()
            .WithDefault(SystemMethods.NewGuid)
            .WithColumn("ClubId")
            .AsGuid()
            .ForeignKey("Clubs", "Id")
            .WithColumn("Name")
            .AsString()
            .NotNullable()
            .WithColumn("DateCreated")
            .AsDateTimeOffset()
            .WithDefault(SystemMethods.CurrentUTCDateTime)
        |> ignore

        this
            .Create
            .Table("ChannelLinks")
            .WithColumn("ChannelId")
            .AsGuid()
            .ForeignKey("Channels", "Id")
            .WithColumn("LinkId")
            .AsGuid()
            .ForeignKey("Links", "Id")
            .WithColumn("UserId")
            .AsGuid()
            .ForeignKey("Users", "Id")
            .WithColumn("DateCreated")
            .AsDateTimeOffset()
            .WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("LastUpdated")
            .AsDateTimeOffset()
            .WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("UpVotes")
            .AsInt32()
            .WithDefaultValue(0)
            .WithColumn("DownVotes")
            .AsInt32()
            .WithDefaultValue(0)
            .WithColumn("Via")
            .AsString()
            .NotNullable()
        |> ignore

        this
            .Create
            .PrimaryKey("ChannelLinks_Composite")
            .OnTable("ChannelLinks")
            .Columns("ChannelId", "LinkId")
        |> ignore


    override this.Down() =
        this.Delete.Table("ChannelLinks")
        |> ignore

        this.Delete.Table("Channels")
        |> ignore

        this.Delete.Table("Links")
        |> ignore

        this.Delete.Table("Clubs")
        |> ignore

        this.Delete.Table("Users")
        |> ignore
