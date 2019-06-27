using System.Collections.Generic;
using System.Xml.Serialization;

namespace Nixtus.Plugin.Payments.Nmi.Dtos
{
    [XmlRoot(ElementName = "customer")]
    public class Customer
    {
        [XmlElement(ElementName = "billing")]
        public List<Billing> Billing { get; set; }
        [XmlElement(ElementName = "shipping")]
        public Shipping Shipping { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }
    }

    [XmlRoot(ElementName = "billing")]
    public class Billing
    {
        [XmlElement(ElementName = "first_name")]
        public string FirstName { get; set; }
        [XmlElement(ElementName = "last_name")]
        public string LastName { get; set; }
        [XmlElement(ElementName = "address_1")]
        public string Address_1 { get; set; }
        [XmlElement(ElementName = "address_2")]
        public string Address_2 { get; set; }
        [XmlElement(ElementName = "company")]
        public string Company { get; set; }
        [XmlElement(ElementName = "city")]
        public string City { get; set; }
        [XmlElement(ElementName = "state")]
        public string State { get; set; }
        [XmlElement(ElementName = "postal_code")]
        public string PostalCode { get; set; }
        [XmlElement(ElementName = "country")]
        public string Country { get; set; }
        [XmlElement(ElementName = "email")]
        public string Email { get; set; }
        [XmlElement(ElementName = "phone")]
        public string Phone { get; set; }
        [XmlElement(ElementName = "fax")]
        public string Fax { get; set; }
        [XmlElement(ElementName = "cc_number")]
        public string CcNumber { get; set; }
        [XmlElement(ElementName = "cc_hash")]
        public string CcHash { get; set; }
        [XmlElement(ElementName = "cc_exp")]
        public string CcExp { get; set; }
        [XmlElement(ElementName = "cc_start_date")]
        public string CcStartDate { get; set; }
        [XmlElement(ElementName = "cc_issue_number")]
        public string CcIssueNumber { get; set; }
        [XmlElement(ElementName = "check_account")]
        public string CheckAccount { get; set; }
        [XmlElement(ElementName = "check_hash")]
        public string CheckHash { get; set; }
        [XmlElement(ElementName = "check_aba")]
        public string CheckAba { get; set; }
        [XmlElement(ElementName = "check_name")]
        public string CheckName { get; set; }
        [XmlElement(ElementName = "account_holder_type")]
        public string AccountHolderType { get; set; }
        [XmlElement(ElementName = "account_type")]
        public string AccountType { get; set; }
        [XmlElement(ElementName = "sec_code")]
        public string SecCode { get; set; }
        [XmlElement(ElementName = "priority")]
        public string Priority { get; set; }
        [XmlElement(ElementName = "cc_bin")]
        public string CcBin { get; set; }
        [XmlElement(ElementName = "created")]
        public string Created { get; set; }
        [XmlElement(ElementName = "updated")]
        public string Updated { get; set; }
        [XmlElement(ElementName = "account_updated")]
        public string AccountUpdated { get; set; }
        [XmlElement(ElementName = "customer_vault_id")]
        public string CustomerVaultId { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }
    }

    [XmlRoot(ElementName = "shipping")]
    public class Shipping
    {
        [XmlElement(ElementName = "first_name")]
        public string FirstName { get; set; }
        [XmlElement(ElementName = "last_name")]
        public string LastName { get; set; }
        [XmlElement(ElementName = "address_1")]
        public string Address_1 { get; set; }
        [XmlElement(ElementName = "address_2")]
        public string Address_2 { get; set; }
        [XmlElement(ElementName = "company")]
        public string Company { get; set; }
        [XmlElement(ElementName = "city")]
        public string City { get; set; }
        [XmlElement(ElementName = "state")]
        public string State { get; set; }
        [XmlElement(ElementName = "postal_code")]
        public string PostalCode { get; set; }
        [XmlElement(ElementName = "country")]
        public string Country { get; set; }
        [XmlElement(ElementName = "email")]
        public string Email { get; set; }
        [XmlElement(ElementName = "phone")]
        public string Phone { get; set; }
        [XmlElement(ElementName = "fax")]
        public string Fax { get; set; }
        [XmlElement(ElementName = "priority")]
        public string Priority { get; set; }
        [XmlElement(ElementName = "created")]
        public string Created { get; set; }
        [XmlElement(ElementName = "updated")]
        public string Updated { get; set; }
        [XmlElement(ElementName = "account_updated")]
        public string AccountUpdated { get; set; }
        [XmlElement(ElementName = "customer_vault_id")]
        public string CustomerVaultId { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }
    }

    [XmlRoot(ElementName = "customer_vault")]
    public class CustomerVault
    {
        [XmlElement(ElementName = "customer")]
        public Customer Customer { get; set; }
    }

    [XmlRoot(ElementName = "nm_response")]
    public class NmiQueryResponse
    {
        [XmlElement(ElementName = "customer_vault")]
        public CustomerVault CustomerVault { get; set; }
    }
}
