﻿using Com.Danliris.Service.Finance.Accounting.Lib.BusinessLogic.Interfaces.CreditBalance;
using Com.Danliris.Service.Finance.Accounting.Lib.Helpers;
using Com.Danliris.Service.Finance.Accounting.Lib.Models.CreditorAccount;
using Com.Danliris.Service.Finance.Accounting.Lib.Services.IdentityService;
using Com.Danliris.Service.Finance.Accounting.Lib.Utilities;
using Com.Danliris.Service.Finance.Accounting.Lib.ViewModels.CreditBalance;
using Com.Moonlay.NetCore.Lib;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace Com.Danliris.Service.Finance.Accounting.Lib.BusinessLogic.Services.CreditBalance
{
    public class CreditBalanceService : ICreditBalanceService
    {
        private const string UserAgent = "finance-service";
        protected DbSet<CreditorAccountModel> DbSet;
        protected IIdentityService IdentityService;
        public FinanceDbContext DbContext;

        public CreditBalanceService(IServiceProvider serviceProvider, FinanceDbContext dbContext)
        {
            DbContext = dbContext;
            DbSet = dbContext.Set<CreditorAccountModel>();
            IdentityService = serviceProvider.GetService<IIdentityService>();
        }

        public List<CreditBalanceViewModel> GetReport(string suplierName, int month, int year, int offSet)
        {
            IQueryable<CreditorAccountModel> query = DbContext.CreditorAccounts.AsQueryable();
            List<CreditBalanceViewModel> result = new List<CreditBalanceViewModel>();
            int previousMonth = month - 1;
            int previousYear = year;

            if (previousMonth == 0)
            {
                previousMonth = 12;
                previousYear = year - 1;
            }

            query = query.Where(x => x.UnitReceiptNoteDate.HasValue && x.UnitReceiptNoteDate.Value.Month == month && x.UnitReceiptNoteDate.Value.Year == year);
            if (!string.IsNullOrEmpty(suplierName))
                query = query.Where(x => x.SupplierName == suplierName);



            foreach (var item in query.GroupBy(x => x.SupplierCode).ToList())
            {
                var productsUnion = string.Join("\n", item.Select(x => x.Products).ToList());
                var uniqueProducts = string.Join("\n", productsUnion.Split("\n").Distinct());

                var creditBalance = new CreditBalanceViewModel()
                {
                    StartBalance = DbSet.AsQueryable().Where(x => x.SupplierCode == item.Key
                                    && x.UnitReceiptNoteDate.HasValue && x.UnitReceiptNoteDate.Value.Month == previousMonth
                                    && x.UnitReceiptNoteDate.Value.Year == previousYear).ToList().Sum(x => x.FinalBalance),
                    Products = uniqueProducts,
                    Purchase = item.Sum(x => x.UnitReceiptMutation),
                    Payment = item.Sum(x => x.BankExpenditureNoteMutation),
                    FinalBalance = item.Sum(x => x.FinalBalance),
                    SupplierName = item.FirstOrDefault() == null ? "" : item.FirstOrDefault().SupplierName ?? "",
                    Currency = item.FirstOrDefault() == null ? "" : item.FirstOrDefault().CurrencyCode ?? ""
                };
                creditBalance.FinalBalance = creditBalance.StartBalance + creditBalance.Purchase - creditBalance.Payment;
                result.Add(creditBalance);
            }

            return result.OrderBy(x=> x.Currency).ThenBy(x => x.Products).ThenBy(x => x.SupplierName).ToList();
        }

        public MemoryStream GenerateExcel(string suplierName, int month, int year, int offSet)
        {
            var data = GetReport(suplierName, month, year, offSet);

            DataTable dt = new DataTable();

            dt.Columns.Add(new DataColumn() { ColumnName = "Mata Uang", DataType = typeof(string) });
            dt.Columns.Add(new DataColumn() { ColumnName = "Supplier", DataType = typeof(string) });
            dt.Columns.Add(new DataColumn() { ColumnName = "Saldo Awal", DataType = typeof(string) });
            dt.Columns.Add(new DataColumn() { ColumnName = "Pembelian", DataType = typeof(string) });
            dt.Columns.Add(new DataColumn() { ColumnName = "Pembayaran", DataType = typeof(string) });
            dt.Columns.Add(new DataColumn() { ColumnName = "Saldo Akhir", DataType = typeof(string) });


            if (data.Count == 0)
            {
                dt.Rows.Add("", "", "", "", "", "");
            }
            else
            {
                foreach (var item in data)
                {
                    dt.Rows.Add(item.Currency, item.SupplierName, item.StartBalance.ToString("#,##0"), item.Purchase.ToString("#,##0"),
                        item.Payment.ToString("#,##0"), item.FinalBalance.ToString("#,##0"));
                }
            }

            return Excel.CreateExcel(new List<KeyValuePair<DataTable, string>>() { new KeyValuePair<DataTable, string>(dt, "Saldo Hutang") }, true);
        }

        public ReadResponse<CreditBalanceViewModel> GetReport(int page, int size, string suplierName, int month, int year, int offSet)
        {
            var queries = GetReport(suplierName, month, year, offSet);

            Pageable<CreditBalanceViewModel> pageable = new Pageable<CreditBalanceViewModel>(queries, page - 1, size);
            List<CreditBalanceViewModel> data = pageable.Data.ToList();

            return new ReadResponse<CreditBalanceViewModel>(queries, pageable.TotalCount, new Dictionary<string, string>(), new List<string>());
        }
    }
}
