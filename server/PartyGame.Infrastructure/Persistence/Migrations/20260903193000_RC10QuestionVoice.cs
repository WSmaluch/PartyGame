using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyGame.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PartyGameDbContext))]
[Migration("20260903193000_RC10QuestionVoice")]
public partial class RC10QuestionVoice : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "QuestionVoiceEnabled",
            table: "RoomSettings",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "QuestionVoiceEnabled",
            table: "RoomSettings");
    }
}
