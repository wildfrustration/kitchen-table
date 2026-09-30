using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PlanD.Api.Data;

/// <summary>App data (agencies, brokers, clients) in the "app" schema. CMS reference data lives in "cms".</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityUserContext<BrokerUser, Guid>(options)
{
    public const string Schema = "app";

    public DbSet<Agency> Agencies => Set<Agency>();
    public DbSet<BrokerCarrier> BrokerCarriers => Set<BrokerCarrier>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientDrug> ClientDrugs => Set<ClientDrug>();
    public DbSet<ClientPharmacy> ClientPharmacies => Set<ClientPharmacy>();
    public DbSet<Consent> Consents => Set<Consent>();
    public DbSet<ClientNote> ClientNotes => Set<ClientNote>();
    public DbSet<PatientLink> PatientLinks => Set<PatientLink>();
    public DbSet<BrokerInvite> BrokerInvites => Set<BrokerInvite>();
    public DbSet<QuoteSnapshot> QuoteSnapshots => Set<QuoteSnapshot>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema(Schema);

        b.Entity<BrokerUser>(e =>
        {
            e.ToTable("brokers");
            e.HasIndex(x => x.PublicSlug).IsUnique();
            e.HasOne(x => x.Agency).WithMany(a => a.Brokers).HasForeignKey(x => x.AgencyId);
            e.Property(x => x.Role).HasConversion<string>();
        });
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("broker_claims");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("broker_logins");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("broker_tokens");

        b.Entity<Agency>().ToTable("agencies");

        b.Entity<BrokerCarrier>(e =>
        {
            e.ToTable("broker_carriers");
            e.HasKey(x => new { x.BrokerId, x.ParentOrganization });
        });

        b.Entity<Client>(e =>
        {
            e.ToTable("clients");
            e.HasIndex(x => new { x.AgencyId, x.BrokerId, x.Status });
            e.HasOne(x => x.Broker).WithMany().HasForeignKey(x => x.BrokerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Agency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.Source).HasConversion<string>();
            e.Property(x => x.MedicareStatus).HasConversion<string>();
            e.Property(x => x.ExtraHelp).HasConversion<string>();
            e.HasMany(x => x.Drugs).WithOne().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Pharmacies).WithOne().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Consents).WithOne().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Notes).WithOne().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ClientDrug>(e =>
        {
            e.ToTable("client_drugs");
            e.Ignore(x => x.QuantityPerFill);
        });
        b.Entity<ClientPharmacy>().ToTable("client_pharmacies");
        b.Entity<Consent>(e =>
        {
            e.ToTable("consents");
            e.Property(x => x.Kind).HasConversion<string>();
        });
        b.Entity<ClientNote>().ToTable("client_notes");

        b.Entity<PatientLink>(e =>
        {
            e.ToTable("patient_links");
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.Purpose).HasConversion<string>();
            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BrokerInvite>(e =>
        {
            e.ToTable("broker_invites");
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.Role).HasConversion<string>();
        });

        b.Entity<QuoteSnapshot>(e =>
        {
            e.ToTable("quote_snapshots");
            e.HasIndex(x => x.ClientId);
            e.Property(x => x.RequestJson).HasColumnType("jsonb");
            e.Property(x => x.ResultJson).HasColumnType("jsonb");
            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
